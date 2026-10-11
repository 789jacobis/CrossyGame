# Cross The Road AWS infrastructure

AWS CDK v2 C# application for the continuously available `production` environment described in [`../docs/AWS_TARGET_ARCHITECTURE.md`](../docs/AWS_TARGET_ARCHITECTURE.md).

## Requirements

- Node.js 22 or later
- .NET SDK 9 or later
- AWS CDK Toolkit installed through this directory's npm dependencies
- AWS credentials are required only for `bootstrap`, `diff`, and `deploy`; local build, test, list, and synth do not deploy resources

## Local verification

```powershell
npm install
dotnet test .\CrossyRoad.Aws.sln --configuration Release
npm run list
npm run synth
```

The Web stack provisions a private, encrypted, versioned S3 bucket and exposes it only through an HTTPS CloudFront distribution using Origin Access Control (OAC). Stack outputs provide the bucket name, distribution ID, and game URL used by the deployment workflow.

After deploying the Web stack, publish a Unity Web build and invalidate the CDN cache with:

```powershell
.\scripts\Publish-WebBuild.ps1
```

The script reads `cdk-outputs.json`, synchronizes `../Builds/Release`, and assigns the required Brotli content encoding and MIME types to Unity's compressed build artifacts.

The `Build and Deploy WebGL` GitHub Actions workflow builds the same production player with Unity, stores a 14-day workflow artifact, assumes the OIDC deployment role, reads the Web stack outputs, publishes to S3, invalidates CloudFront, and verifies the public `index.html`. It requires the `UNITY_LICENSE`, `UNITY_EMAIL`, and `UNITY_PASSWORD` repository secrets. Set the repository variable `ENABLE_WEBGL_AUTO_DEPLOY=true` to publish Unity changes pushed to `main`; otherwise the workflow remains available through manual dispatch.

The production backend includes an anonymous Cognito Identity Pool, protected on-demand DynamoDB tables, an IAM-authorized HTTP API with default throttling, and a .NET 10 Lambda that reuses the shared score-validation domain. The monitoring stack adds a US$5 budget with US$1/3/5 thresholds, four operational CloudWatch alarms, and SNS email delivery. Publish the Lambda project to `.artifacts/backend` before synthesizing or deploying the API stack.

`CrossyRoadCiStack-production` creates a GitHub OIDC provider and a deployment role trusted only by `789jacobis/CrossyGame` on `main`. The workflows assume that role with a short-lived token. It can assume the standard CDK bootstrap roles and has direct least-privilege access to synchronize the production Web bucket, invalidate its CloudFront distribution, and read that Web stack's outputs. No long-lived AWS access key is stored in GitHub.

## Context

Defaults are stored in `cdk.json`:

| Key | Default |
| --- | --- |
| `projectName` | `CrossyRoad` |
| `environmentName` | `production` |
| `region` | `ap-northeast-1` |

The AWS account is resolved from `CDK_DEFAULT_ACCOUNT` when credentials are configured. The public account ID is present in the deployment workflow's role ARN; no access key, secret, session token, or other AWS credential is committed.
