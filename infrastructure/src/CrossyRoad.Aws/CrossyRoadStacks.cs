using Amazon.CDK;
using Amazon.CDK.AWS.Cognito;
using Amazon.CDK.AWS.Budgets;
using Amazon.CDK.AWS.DynamoDB;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.CloudWatch;
using Amazon.CDK.AWS.CloudWatch.Actions;
using Amazon.CDK.AWS.SNS;
using Amazon.CDK.AWS.SNS.Subscriptions;
using Constructs;
using DynamoAttribute = Amazon.CDK.AWS.DynamoDB.Attribute;

namespace CrossyRoad.Aws;

public abstract class CrossyRoadStack : Stack
{
    protected CrossyRoadStack(
        Construct scope,
        string id,
        DeploymentSettings settings,
        IStackProps props)
        : base(scope, id, props)
    {
        Settings = settings;

        Amazon.CDK.Tags.Of(this).Add("Project", settings.ProjectName);
        Amazon.CDK.Tags.Of(this).Add(
            "Environment",
            settings.EnvironmentName);
        Amazon.CDK.Tags.Of(this).Add("ManagedBy", "AWS-CDK");
    }

    protected DeploymentSettings Settings { get; }
}

public sealed class CrossyRoadDataStack : CrossyRoadStack
{
    public Table RunsTable { get; }

    public Table ScoresTable { get; }

    public CrossyRoadDataStack(
        Construct scope,
        string id,
        DeploymentSettings settings,
        IStackProps props)
        : base(scope, id, settings, props)
    {
        RunsTable = new Table(this, "Runs", new TableProps
        {
            PartitionKey = new DynamoAttribute
            {
                Name = "PlayerId",
                Type = AttributeType.STRING
            },
            SortKey = new DynamoAttribute
            {
                Name = "RunId",
                Type = AttributeType.STRING
            },
            BillingMode = BillingMode.PAY_PER_REQUEST,
            Encryption = TableEncryption.AWS_MANAGED,
            PointInTimeRecoverySpecification = new PointInTimeRecoverySpecification
            {
                PointInTimeRecoveryEnabled = true
            },
            DeletionProtection = true,
            RemovalPolicy = RemovalPolicy.RETAIN,
            TimeToLiveAttribute = "ExpiresAt"
        });

        ScoresTable = new Table(this, "Scores", new TableProps
        {
            PartitionKey = new DynamoAttribute
            {
                Name = "LeaderboardId",
                Type = AttributeType.STRING
            },
            SortKey = new DynamoAttribute
            {
                Name = "PlayerId",
                Type = AttributeType.STRING
            },
            BillingMode = BillingMode.PAY_PER_REQUEST,
            Encryption = TableEncryption.AWS_MANAGED,
            PointInTimeRecoverySpecification = new PointInTimeRecoverySpecification
            {
                PointInTimeRecoveryEnabled = true
            },
            DeletionProtection = true,
            RemovalPolicy = RemovalPolicy.RETAIN
        });

        ScoresTable.AddGlobalSecondaryIndex(new GlobalSecondaryIndexProps
        {
            IndexName = "LeaderboardRankIndex",
            PartitionKey = new DynamoAttribute
            {
                Name = "LeaderboardId",
                Type = AttributeType.STRING
            },
            SortKey = new DynamoAttribute
            {
                Name = "ScoreRank",
                Type = AttributeType.STRING
            },
            ProjectionType = ProjectionType.ALL
        });

        _ = new CfnOutput(this, "RunsTableName", new CfnOutputProps
        {
            Value = RunsTable.TableName
        });

        _ = new CfnOutput(this, "ScoresTableName", new CfnOutputProps
        {
            Value = ScoresTable.TableName
        });
    }
}

public sealed class CrossyRoadIdentityStack : CrossyRoadStack
{
    public CfnIdentityPool IdentityPool { get; }

    public Role GuestRole { get; }

    public CrossyRoadIdentityStack(
        Construct scope,
        string id,
        DeploymentSettings settings,
        IStackProps props)
        : base(scope, id, settings, props)
    {
        IdentityPool = new CfnIdentityPool(this, "PlayerIdentityPool", new CfnIdentityPoolProps
        {
            AllowUnauthenticatedIdentities = true,
            IdentityPoolName = $"{settings.ProjectName}_{settings.EnvironmentName}_players"
        });

        GuestRole = new Role(this, "GuestPlayerRole", new RoleProps
        {
            Description = "Least-privilege role for anonymous Cross The Road players.",
            AssumedBy = new FederatedPrincipal(
                "cognito-identity.amazonaws.com",
                new Dictionary<string, object>
                {
                    ["StringEquals"] = new Dictionary<string, object>
                    {
                        ["cognito-identity.amazonaws.com:aud"] = IdentityPool.Ref
                    },
                    ["ForAnyValue:StringLike"] = new Dictionary<string, object>
                    {
                        ["cognito-identity.amazonaws.com:amr"] = "unauthenticated"
                    }
                },
                "sts:AssumeRoleWithWebIdentity")
        });

        _ = new CfnIdentityPoolRoleAttachment(this, "PlayerIdentityRoles", new CfnIdentityPoolRoleAttachmentProps
        {
            IdentityPoolId = IdentityPool.Ref,
            Roles = new Dictionary<string, string>
            {
                ["unauthenticated"] = GuestRole.RoleArn
            }
        });

        _ = new CfnOutput(this, "IdentityPoolId", new CfnOutputProps
        {
            Value = IdentityPool.Ref
        });

        _ = new CfnOutput(this, "GuestPlayerRoleArn", new CfnOutputProps
        {
            Value = GuestRole.RoleArn
        });
    }
}

public sealed class CrossyRoadMonitoringStack : CrossyRoadStack
{
    public CrossyRoadMonitoringStack(
        Construct scope,
        string id,
        DeploymentSettings settings,
        IStackProps props,
        CrossyRoadApiStack api)
        : base(scope, id, settings, props)
    {
        var budgetEmail1 = new CfnParameter(this, "BudgetEmail1", new CfnParameterProps
        {
            Type = "String",
            Description = "Primary recipient for AWS cost alerts.",
            AllowedPattern = "^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$",
            NoEcho = true
        });
        var budgetEmail2 = new CfnParameter(this, "BudgetEmail2", new CfnParameterProps
        {
            Type = "String",
            Description = "Secondary recipient for AWS cost alerts.",
            AllowedPattern = "^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$",
            NoEcho = true
        });

        CfnBudget.SubscriberProperty[] Subscribers() =>
        [
            new CfnBudget.SubscriberProperty
            {
                Address = budgetEmail1.ValueAsString,
                SubscriptionType = "EMAIL"
            },
            new CfnBudget.SubscriberProperty
            {
                Address = budgetEmail2.ValueAsString,
                SubscriptionType = "EMAIL"
            }
        ];

        CfnBudget.NotificationWithSubscribersProperty AlertAt(double amount) => new()
        {
            Notification = new CfnBudget.NotificationProperty
            {
                ComparisonOperator = "GREATER_THAN",
                NotificationType = "ACTUAL",
                Threshold = amount,
                ThresholdType = "ABSOLUTE_VALUE"
            },
            Subscribers = Subscribers()
        };

        _ = new CfnBudget(this, "MonthlyCostBudget", new CfnBudgetProps
        {
            Budget = new CfnBudget.BudgetDataProperty
            {
                BudgetName = $"{settings.ProjectName}-{settings.EnvironmentName}-monthly",
                BudgetType = "COST",
                TimeUnit = "MONTHLY",
                BudgetLimit = new CfnBudget.SpendProperty
                {
                    Amount = 5,
                    Unit = "USD"
                }
            },
            NotificationsWithSubscribers = new[]
            {
                AlertAt(1),
                AlertAt(3),
                AlertAt(5)
            }
        });

        var alertTopic = new Topic(this, "OperationalAlerts", new TopicProps
        {
            DisplayName = "Cross The Road production alerts",
            TopicName = $"{settings.ProjectName}-{settings.EnvironmentName}-operational-alerts"
        });
        alertTopic.AddSubscription(new EmailSubscription(budgetEmail1.ValueAsString));
        alertTopic.AddSubscription(new EmailSubscription(budgetEmail2.ValueAsString));

        var alarmAction = new SnsAction(alertTopic);

        Alarm CreateAlarm(string id, string description, IMetric metric, double threshold)
        {
            var alarm = new Alarm(this, id, new AlarmProps
            {
                AlarmDescription = description,
                Metric = metric,
                Threshold = threshold,
                EvaluationPeriods = 1,
                DatapointsToAlarm = 1,
                ComparisonOperator = ComparisonOperator.GREATER_THAN_OR_EQUAL_TO_THRESHOLD,
                TreatMissingData = TreatMissingData.NOT_BREACHING
            });
            alarm.AddAlarmAction(alarmAction);
            return alarm;
        }

        CreateAlarm(
            "BackendErrors",
            "The production game backend returned at least one Lambda error in five minutes.",
            api.Handler.MetricErrors(new MetricOptions
            {
                Period = Duration.Minutes(5),
                Statistic = "Sum"
            }),
            1);
        CreateAlarm(
            "BackendThrottles",
            "The production game backend throttled at least one invocation in five minutes.",
            api.Handler.MetricThrottles(new MetricOptions
            {
                Period = Duration.Minutes(5),
                Statistic = "Sum"
            }),
            1);
        CreateAlarm(
            "ApiServerErrors",
            "The production HTTP API returned at least one 5xx response in five minutes.",
            new Metric(new MetricProps
            {
                Namespace = "AWS/ApiGateway",
                MetricName = "5xx",
                DimensionsMap = new Dictionary<string, string>
                {
                    ["ApiId"] = api.Api.ApiId
                },
                Period = Duration.Minutes(5),
                Statistic = "Sum",
                Unit = Unit.COUNT
            }),
            1);
        CreateAlarm(
            "ApiHighLatency",
            "The production HTTP API p95 latency exceeded two seconds in five minutes.",
            new Metric(new MetricProps
            {
                Namespace = "AWS/ApiGateway",
                MetricName = "Latency",
                DimensionsMap = new Dictionary<string, string>
                {
                    ["ApiId"] = api.Api.ApiId
                },
                Period = Duration.Minutes(5),
                Statistic = "p95",
                Unit = Unit.MILLISECONDS
            }),
            2000);

        _ = new CfnOutput(this, "OperationalAlertTopicArn", new CfnOutputProps
        {
            Value = alertTopic.TopicArn
        });
    }
}
