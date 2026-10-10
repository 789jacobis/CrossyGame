using Amazon.CDK;

namespace CrossyRoad.Aws;

public sealed record CrossyRoadStacks(
    CrossyRoadDataStack Data,
    CrossyRoadIdentityStack Identity,
    CrossyRoadApiStack Api,
    CrossyRoadWebStack Web,
    CrossyRoadMonitoringStack Monitoring);

public static class CrossyRoadInfrastructure
{
    public static CrossyRoadStacks DefineStacks(
        App app,
        DeploymentSettings settings)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(settings);
        settings.EnsureValid();

        var environment = new Amazon.CDK.Environment
        {
            Account = settings.Account,
            Region = settings.Region
        };

        StackProps Props() => new()
        {
            Env = environment,
            TerminationProtection = true
        };

        var data = new CrossyRoadDataStack(
            app,
            settings.StackName("Data"),
            settings,
            Props());

        var identity = new CrossyRoadIdentityStack(
            app,
            settings.StackName("Identity"),
            settings,
            Props());

        var api = new CrossyRoadApiStack(
            app,
            settings.StackName("Api"),
            settings,
            Props(),
            data,
            identity);
        api.AddStackDependency(data);
        api.AddStackDependency(identity);

        var web = new CrossyRoadWebStack(
            app,
            settings.StackName("Web"),
            settings,
            Props());

        var monitoring = new CrossyRoadMonitoringStack(
            app,
            settings.StackName("Monitoring"),
            settings,
            Props(),
            api);
        monitoring.AddStackDependency(api);
        monitoring.AddStackDependency(data);

        return new CrossyRoadStacks(
            data,
            identity,
            api,
            web,
            monitoring);
    }
}
