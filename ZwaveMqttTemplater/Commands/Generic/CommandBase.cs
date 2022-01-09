using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ZwaveMqttTemplater.Z2M;

namespace ZwaveMqttTemplater.Commands.Generic;

abstract internal class CommandBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IHostApplicationLifetime _lifeTime;

    protected CommandBase(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _lifeTime = serviceProvider.GetRequiredService<IHostApplicationLifetime>();
    }

    protected async Task<Z2MApiClient> GetApiClient()
    {
        Z2MApiClient apiClient = _serviceProvider.GetRequiredService<Z2MApiClient>();
        await apiClient.Start(_lifeTime.ApplicationStopping);

        return apiClient;
    }

    public Task ExecuteAsync()
    {
        return OnExecuteAsync(_lifeTime.ApplicationStopping);
    }

    protected abstract Task OnExecuteAsync(CancellationToken token);
}