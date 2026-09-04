using AiDevAgent.Application.Agents;
using AiDevAgent.Application.Git;
using AiDevAgent.Agents.Support;
using AiDevAgent.Domain.Interfaces;
using AiDevAgent.Domain.ValueObjects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AiDevAgent.Agents.Git;

public sealed class GitAgent(
    IPromptStore prompts,
    LlmAgentRunner llm,
    IGitService git,
    IGitLabClient gitLab,
    ISecretProvider secrets,
    IConfiguration configuration,
    ILogger<GitAgent> logger) : IGitAgent
{
    public async Task<AgentOutcome<GitExecutionResult>> RunAsync(
        GitWorkItem work,
        CancellationToken cancellationToken = default)
    {
        var item = work.Item;
        var numericId = Math.Abs(BitConverter.ToInt32(item.TaskId.ToByteArray(), 0)) % 100_000;
        var messages = await BuildMessagesAsync(work, cancellationToken);

        var gitDir = Path.Combine(item.WorkspacePath, ".git");
        var hasRepo = Directory.Exists(gitDir);
        var token = await secrets.GetSecretAsync("GitLab:Token", cancellationToken)
            ?? configuration["GitLab:Token"];
        var projectId = ResolveGitLabProjectId(work.GitLabProjectId, work.RepositoryUrl);
        var targetBranch = GitBranches.OrFallback(work.DefaultBranch);
        var branch = work.ContinueOnTargetBranch
            ? targetBranch
            : BranchName.Create(numericId, BranchName.NormalizeSlug(item.TaskTitle));
        var canCreateMr = hasRepo
            && !work.ContinueOnTargetBranch
            && !string.IsNullOrWhiteSpace(token)
            && !string.IsNullOrWhiteSpace(projectId);

        string commitSha;
        string? mrUrl = null;
        long? mrIid = null;
        string mrState = "opened";
        var pushed = false;

        if (hasRepo)
        {
            if (!work.ContinueOnTargetBranch)
                await git.CreateBranchAsync(item.WorkspacePath, branch, cancellationToken);

            var status = await git.StatusAsync(item.WorkspacePath, cancellationToken);
            if (!string.IsNullOrWhiteSpace(status))
                await git.CommitAsync(item.WorkspacePath, messages.CommitMessage, cancellationToken);

            commitSha = await git.GetHeadShaAsync(item.WorkspacePath, cancellationToken);

            try
            {
                await git.PushAsync(item.WorkspacePath, branch, cancellationToken);
                pushed = true;
            }
            catch (Exception ex) when (!work.ContinueOnTargetBranch)
            {
                logger.LogWarning(ex, "git push failed for workflow {WorkflowId}; continuing with local commit.", item.WorkflowId);
            }

            if (canCreateMr)
            {
                var mr = await gitLab.CreateMergeRequestAsync(
                    projectId,
                    branch,
                    targetBranch,
                    messages.MergeRequestTitle,
                    messages.MergeRequestDescription,
                    cancellationToken);
                mrUrl = mr.Url;
                mrIid = mr.Iid;
                mrState = mr.State;
            }
            else if (work.ContinueOnTargetBranch && pushed)
            {
                mrUrl = GitCloneUrl.CommitUrl(work.RepositoryUrl, commitSha);
                mrState = "pushed";
            }
        }
        else
        {
            commitSha = "local-" + Guid.NewGuid().ToString("N")[..12];
        }

        if (string.IsNullOrWhiteSpace(mrUrl))
        {
            mrUrl = $"https://example.invalid/merge_requests/stub/{item.WorkflowId:N}";
            logger.LogInformation(
                "Git Agent used stub MR URL. hasRepo={HasRepo} tokenConfigured={HasToken} projectId={ProjectId}",
                hasRepo,
                !string.IsNullOrWhiteSpace(token),
                projectId);
        }

        var stub = mrUrl.Contains("example.invalid", StringComparison.Ordinal);
        var result = new GitExecutionResult(
            true,
            branch,
            commitSha,
            mrUrl,
            mrIid,
            messages.MergeRequestTitle,
            messages.MergeRequestDescription,
            mrState);
        return new AgentOutcome<GitExecutionResult>(
            result,
            Summary: work.ContinueOnTargetBranch && pushed && !stub
                ? $"Pushed commit {ShortSha(commitSha)} to {branch}."
                : stub
                    ? StubSummary(hasRepo, token, projectId, branch)
                    : $"Created branch {branch} and merge request.");
    }

    private static string ShortSha(string sha)
        => sha.Length <= 7 ? sha : sha[..7];

    private static string ResolveGitLabProjectId(string configuredId, string repositoryUrl)
    {
        if (!string.IsNullOrWhiteSpace(configuredId) && configuredId is not "0")
            return configuredId.Trim();
        return GitCloneUrl.ProjectPathFromUrl(repositoryUrl) ?? string.Empty;
    }

    private static string StubSummary(bool hasRepo, string? token, string projectId, string branch)
    {
        if (!hasRepo)
            return $"Created branch {branch} (stub MR; repository was not cloned).";
        if (string.IsNullOrWhiteSpace(token))
            return $"Created branch {branch} (stub MR; GitLab:Token is missing).";
        if (string.IsNullOrWhiteSpace(projectId))
            return $"Created branch {branch} (stub MR; set a GitLab project ID or a gitlab.com repository URL).";
        return $"Created branch {branch} (stub MR).";
    }

    private async Task<GitMessageDto> BuildMessagesAsync(GitWorkItem work, CancellationToken cancellationToken)
    {
        var fallback = new GitMessageDto
        {
            CommitMessage = string.IsNullOrWhiteSpace(work.Item.UserInstructions)
                ? $"feat: {work.Item.TaskTitle}"
                : $"feat: {work.Item.TaskTitle}\n\n{work.Item.UserInstructions}",
            MergeRequestTitle = work.Item.TaskTitle,
            MergeRequestDescription =
                $"Automated change for **{work.Item.TaskTitle}**.\n\n{work.Item.TaskDescription}\n\nTests passed: {work.TestsPassed}."
        };

        if (!llm.IsConfigured)
            return fallback;

        try
        {
            var (dto, _, _) = await llm.RunAsync<GitMessageDto>(
                "Git",
                prompts.Get("git"),
                $"""
                Propose a commit message, MR title, and MR description.
                Do not include secrets. Do not merge.
                Task: {work.Item.TaskTitle}
                Description: {work.Item.TaskDescription}
                """,
                tools: null,
                cancellationToken);

            if (string.IsNullOrWhiteSpace(dto.CommitMessage))
                dto.CommitMessage = fallback.CommitMessage;
            if (string.IsNullOrWhiteSpace(dto.MergeRequestTitle))
                dto.MergeRequestTitle = fallback.MergeRequestTitle;
            if (string.IsNullOrWhiteSpace(dto.MergeRequestDescription))
                dto.MergeRequestDescription = fallback.MergeRequestDescription;
            return dto;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Git Agent LLM messaging failed; using fallback text.");
            return fallback;
        }
    }
}
