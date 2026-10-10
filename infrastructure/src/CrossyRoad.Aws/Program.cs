using Amazon.CDK;
using CrossyRoad.Aws;

var app = new App();
DeploymentSettings settings = DeploymentSettings.FromContext(app.Node);

CrossyRoadInfrastructure.DefineStacks(app, settings);
app.Synth();
