using AiDevAgent.Domain.Interfaces;
using Microsoft.Extensions.Configuration;

namespace AiDevAgent.Infrastructure.Secrets;

public sealed class ConfigurationSecretProvider(IConfiguration configuration) : ISecretProvider
{
    public Task<string?> GetSecretAsync(string name, CancellationToken cancellationToken = default)
    {
        var value = configuration[name];
        return Task.FromResult(string.IsNullOrWhiteSpace(value) ? null : value);
    }
}
