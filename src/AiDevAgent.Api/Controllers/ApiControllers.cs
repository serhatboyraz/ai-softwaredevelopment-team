using AiDevAgent.Application.DTOs;
using AiDevAgent.Application.Jira;
using AiDevAgent.Application.Workflows;
using AiDevAgent.Domain.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AiDevAgent.Api.Controllers;

[ApiController]
[Route("api/projects")]
public sealed class ProjectsController(ProjectService projects) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProjectDto>>> List(CancellationToken cancellationToken)
        => Ok(await projects.ListAsync(cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProjectDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var project = await projects.GetAsync(id, cancellationToken);
        return project is null ? NotFound() : Ok(project);
    }

    [HttpPost]
    public async Task<ActionResult<ProjectDto>> Create([FromBody] CreateProjectRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.RepositoryUrl))
            return BadRequest("Name and RepositoryUrl are required.");

        var created = await projects.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
        => await projects.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();
}

[ApiController]
[Route("api/projects/{projectId:guid}/tasks")]
public sealed class TasksController(TaskService tasks) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TaskDto>>> List(Guid projectId, CancellationToken cancellationToken)
        => Ok(await tasks.ListByProjectAsync(projectId, cancellationToken));

    [HttpPost]
    public async Task<ActionResult<TaskDto>> Create(Guid projectId, [FromBody] CreateTaskRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Description))
            return BadRequest("Title and Description are required.");

        var created = await tasks.CreateAsync(projectId, request, cancellationToken);
        return created is null ? NotFound() : Created($"/api/projects/{projectId}/tasks/{created.Id}", created);
    }
}

[ApiController]
[Route("api/workflows")]
public sealed class WorkflowsController(WorkflowService workflows) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<WorkflowDto>>> List(CancellationToken cancellationToken)
        => Ok(await workflows.ListAsync(cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<WorkflowDetailDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var detail = await workflows.GetDetailAsync(id, cancellationToken);
        return detail is null ? NotFound() : Ok(detail);
    }

    [HttpPost]
    public async Task<ActionResult<WorkflowDto>> Create([FromBody] CreateWorkflowRequest request, CancellationToken cancellationToken)
    {
        var created = await workflows.StartAsync(request, cancellationToken);
        return created is null ? NotFound() : AcceptedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
        => await workflows.CancelAsync(id, cancellationToken) ? NoContent() : NotFound();

    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, [FromBody] ApproveWorkflowRequest request, CancellationToken cancellationToken)
        => await workflows.ApproveAsync(id, request, cancellationToken) ? NoContent() : NotFound();

    [HttpPost("{id:guid}/rerun")]
    public async Task<IActionResult> Rerun(Guid id, CancellationToken cancellationToken)
    {
        var result = await workflows.RerunAsync(id, cancellationToken);
        if (!result.Found)
            return NotFound();
        if (!result.Accepted)
            return Conflict(result.Message);
        return AcceptedAtAction(nameof(Get), new { id }, result.Workflow);
    }

    [HttpPost("{id:guid}/commands")]
    public async Task<IActionResult> SendCommand(Guid id, [FromBody] SendWorkflowCommandRequest request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Command))
            return BadRequest("Command is required.");

        var result = await workflows.SendCommandAsync(id, request.Command, cancellationToken);
        if (!result.Found)
            return NotFound();
        if (!result.Accepted)
            return Conflict(result.Message);
        return AcceptedAtAction(nameof(Get), new { id }, result.Workflow);
    }

    [HttpGet("{id:guid}/events")]
    public async Task<ActionResult<IReadOnlyList<WorkflowEventDto>>> Events(Guid id, CancellationToken cancellationToken)
    {
        var detail = await workflows.GetDetailAsync(id, cancellationToken);
        return detail is null ? NotFound() : Ok(detail.Events);
    }

    [HttpGet("{id:guid}/tests")]
    public async Task<ActionResult<IReadOnlyList<TestRunDto>>> Tests(Guid id, CancellationToken cancellationToken)
    {
        var detail = await workflows.GetDetailAsync(id, cancellationToken);
        return detail is null ? NotFound() : Ok(detail.TestRuns);
    }
}

[ApiController]
[Route("api/merge-requests")]
public sealed class MergeRequestsController(WorkflowService workflows) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MergeRequestDto>>> List(CancellationToken cancellationToken)
    {
        var all = await workflows.ListAsync(cancellationToken);
        var results = new List<MergeRequestDto>();
        foreach (var wf in all)
        {
            var detail = await workflows.GetDetailAsync(wf.Id, cancellationToken);
            if (detail?.MergeRequest is not null)
                results.Add(detail.MergeRequest);
        }
        return Ok(results);
    }
}

[ApiController]
[Route("api/jira")]
public sealed class JiraController(JiraService jira) : ControllerBase
{
    [HttpGet("status")]
    public ActionResult<JiraStatusDto> Status() => Ok(jira.GetStatus());

    [HttpGet("boards")]
    public Task<ActionResult<IReadOnlyList<JiraBoardDto>>> Boards(CancellationToken cancellationToken)
        => Invoke(async () => (IReadOnlyList<JiraBoardDto>?)await jira.ListBoardsAsync(cancellationToken));

    [HttpGet("boards/{boardId:int}/work")]
    public Task<ActionResult<JiraBoardWorkDto>> Work(int boardId, CancellationToken cancellationToken)
        => Invoke(() => jira.GetBoardWorkAsync(boardId, cancellationToken));

    [HttpGet("issues/{issueKey}")]
    public Task<ActionResult<JiraIssueDetailsDto>> Issue(string issueKey, CancellationToken cancellationToken)
        => Invoke(() => jira.GetIssueAsync(issueKey, cancellationToken));

    private async Task<ActionResult<T>> Invoke<T>(Func<Task<T?>> action)
    {
        var status = jira.GetStatus();
        if (!status.Configured)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, status.Message);

        try
        {
            var result = await action();
            return result is null ? NotFound() : Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, ex.Message);
        }
        catch (JiraApiException ex)
        {
            var code = ex.StatusCode is >= 400 and < 600 ? ex.StatusCode : StatusCodes.Status502BadGateway;
            return StatusCode(code, ex.Message);
        }
    }
}

[ApiController]
[Route("api/projects/{projectId:guid}/workflows")]
public sealed class ProjectWorkflowsController(
    TaskService tasks,
    WorkflowService workflows,
    JiraService jira) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<WorkflowDto>> Start(
        Guid projectId,
        [FromBody] StartProjectWorkflowRequest request,
        CancellationToken cancellationToken)
    {
        var jiraKey = request.JiraIssueKey?.Trim();
        if (!string.IsNullOrWhiteSpace(jiraKey))
        {
            try
            {
                var created = await jira.StartWorkflowAsync(projectId, jiraKey, cancellationToken);
                return created is null ? NotFound() : Accepted($"/api/workflows/{created.Id}", created);
            }
            catch (InvalidOperationException ex)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, ex.Message);
            }
            catch (JiraApiException ex)
            {
                var code = ex.StatusCode is >= 400 and < 600 ? ex.StatusCode : StatusCodes.Status502BadGateway;
                return StatusCode(code, ex.Message);
            }
        }

        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Description))
            return BadRequest("Provide JiraIssueKey, or Title and Description.");

        var task = await tasks.CreateAsync(
            projectId,
            new CreateTaskRequest(request.Title, request.Description),
            cancellationToken);
        if (task is null)
            return NotFound();

        var workflow = await workflows.StartAsync(new CreateWorkflowRequest(task.Id), cancellationToken);
        return workflow is null ? NotFound() : Accepted($"/api/workflows/{workflow.Id}", workflow);
    }
}
