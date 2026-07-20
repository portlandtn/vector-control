using System.Text.Json;

public static class GitHubEventFactory
{
    public static NotificationEventRequest? Build(string name, string delivery, JsonElement root, Random? random = null)
    {
        random ??= Random.Shared;
        var repo = TryString(root, "repository", "name") ?? "a repository";
        var action = TryString(root, "action");
        string? type = null, message = null, severity = "info", emotion = "attention";

        switch (name)
        {
            case "ping":
                type = "github.connected";
                emotion = "excited";
                message = Pick(random,
                    $"Jed, GitHub and Vector are now on speaking terms for {repo}. What could go wrong?",
                    $"Jed, the webhook for {repo} is alive. The machines have exchanged phone numbers.",
                    $"GitHub is connected for {repo}, Jed. Vector is officially in the gossip loop.",
                    $"Jed, {repo} just shook hands with Vector. Neither side read the terms and conditions.",
                    $"Connection confirmed for {repo}. Vector can now judge your commits in real time.");
                break;

            case "push":
                type = "github.push";
                message = Pick(random,
                    $"Jed, fresh code landed in {repo}. May the tests be merciful.",
                    $"Someone pushed code to {repo}. I have notified the appropriate tiny robot.",
                    $"Jed, {repo} has new code. The old bugs are excited to meet the new ones.",
                    $"A push just hit {repo}. Bold of someone to assume it compiles.",
                    $"New code in {repo}, Jed. The repository has accepted another offering.");
                break;

            case "pull_request":
                var number = TryInt(root, "pull_request", "number");
                var merged = TryBool(root, "pull_request", "merged");
                if (action == "opened")
                {
                    type = "github.pull_request.opened";
                    message = Pick(random,
                        $"Jed, pull request {number} opened in {repo}. A brave new pile of opinions awaits.",
                        $"Pull request {number} is up in {repo}. Let the ceremonial nitpicking begin.",
                        $"Jed, {repo} has produced pull request {number}. It claims to be ready.",
                        $"Pull request {number} just arrived in {repo}, wearing its nicest diff.",
                        $"A new challenger appears: pull request {number} in {repo}.");
                }
                else if (action == "closed" && merged == true)
                {
                    type = "github.pull_request.merged";
                    emotion = "celebrate";
                    message = Pick(random,
                        $"Jed, pull request {number} merged into {repo}. The code is now everyone's problem.",
                        $"Pull request {number} made it into {repo}. Release the tiny confetti.",
                        $"Jed, {repo} swallowed pull request {number} without even chewing.",
                        $"Pull request {number} merged in {repo}. There is no going back now.",
                        $"Victory, allegedly. Pull request {number} is merged in {repo}.");
                }
                break;

            case "pull_request_review":
                var pr = TryInt(root, "pull_request", "number");
                var reviewState = TryString(root, "review", "state");
                if (action == "submitted" && reviewState == "approved")
                {
                    type = "github.pull_request.approved";
                    emotion = "excited";
                    message = Pick(random,
                        $"Pull request {pr} was approved in {repo}. Someone has chosen trust over evidence.",
                        $"Jed, pull request {pr} got the green light in {repo}. Suspiciously generous.",
                        $"Approval granted for pull request {pr} in {repo}. Frame this rare moment.",
                        $"Pull request {pr} passed human inspection in {repo}. Miracles remain possible.",
                        $"Jed, a reviewer approved pull request {pr} in {repo}. No bribes were detected.");
                }
                else if (action == "submitted" && reviewState == "changes_requested")
                {
                    type = "github.pull_request.changes_requested";
                    message = Pick(random,
                        $"Jed, pull request {pr} needs changes in {repo}. The reviewer brought a red pen and feelings.",
                        $"Changes requested on pull request {pr} in {repo}. Apparently perfection had notes.",
                        $"Pull request {pr} in {repo} has been returned to the kitchen.",
                        $"Jed, someone found character-building opportunities in pull request {pr} for {repo}.",
                        $"Pull request {pr} needs another lap around {repo}. So close, and yet so reviewed.");
                }
                break;

            case "issues":
                var issue = TryInt(root, "issue", "number");
                if (action == "opened")
                {
                    type = "github.issue.opened";
                    message = Pick(random,
                        $"Jed, issue {issue} opened in {repo}. The backlog has spawned again.",
                        $"New issue {issue} in {repo}. Because the existing problems looked lonely.",
                        $"Jed, {repo} has discovered issue {issue}. Discovery is progress, technically.",
                        $"Issue {issue} just joined {repo}. There goes the neighborhood.",
                        $"A fresh problem has entered the chat: issue {issue} in {repo}.");
                }
                else if (action == "assigned")
                {
                    type = "github.issue.assigned";
                    message = Pick(random,
                        $"Jed, issue {issue} was assigned in {repo}. The hot potato has a new owner.",
                        $"Issue {issue} in {repo} found a volunteer. Whether they volunteered is unclear.",
                        $"Someone now owns issue {issue} in {repo}. Condolences have been sent.",
                        $"Jed, issue {issue} has been assigned in {repo}. Accountability strikes again.",
                        $"Issue {issue} in {repo} has selected its next victim.");
                }
                else if (action == "closed")
                {
                    type = "github.issue.closed";
                    emotion = "celebrate";
                    message = Pick(random,
                        $"Jed, issue {issue} closed in {repo}. One hydra head successfully removed.",
                        $"Issue {issue} is closed in {repo}. The backlog barely noticed.",
                        $"Jed, {repo} has one fewer problem: issue {issue} is history.",
                        $"Issue {issue} was closed in {repo}. Please observe a respectful second of productivity.",
                        $"Good news, Jed. Issue {issue} in {repo} has ceased bothering everyone.");
                }
                break;

            case "workflow_run" when action == "completed":
                var workflow = TryString(root, "workflow_run", "name") ?? "The build";
                var conclusion = TryString(root, "workflow_run", "conclusion");
                if (conclusion == "failure")
                {
                    type = "github.workflow.failed";
                    severity = "warning";
                    emotion = "annoyed";
                    message = BuildFailed(random, repo, workflow);
                }
                else if (conclusion == "success")
                {
                    type = "github.workflow.succeeded";
                    emotion = "celebrate";
                    message = BuildSucceeded(random, repo, workflow);
                }
                else if (conclusion == "cancelled")
                {
                    type = "github.workflow.cancelled";
                    emotion = "quiet";
                    message = Pick(random,
                        $"Jed, {workflow} was cancelled in {repo}. It died doing what it loved: not finishing.",
                        $"{workflow} got cancelled in {repo}. Probably for the best, possibly out of fear.",
                        $"Jed, someone pulled the plug on {workflow} in {repo}. The electrons are relieved.",
                        $"{workflow} was cancelled in {repo}. We may never know what fresh nonsense it contained.",
                        $"Build aborted: {workflow} in {repo}. Everyone slowly back away from the button.");
                }
                break;

            case "deployment" when action == "created":
                type = "github.deployment.started";
                emotion = "quiet";
                message = Pick(random,
                    $"Jed, a deployment started for {repo}. The code is leaving its supervised environment.",
                    $"Deployment underway for {repo}. Production has been warned.",
                    $"Jed, {repo} is deploying. Please keep your hands inside the ride.",
                    $"A deployment just started for {repo}. This is where confidence meets infrastructure.",
                    $"{repo} is heading toward production. Godspeed, little commit.");
                break;

            case "deployment_status":
                var deploymentState = TryString(root, "deployment_status", "state");
                if (deploymentState == "success")
                {
                    type = "github.deployment.succeeded";
                    emotion = "celebrate";
                    message = Pick(random,
                        $"Jed, {repo} deployed successfully. Production has accepted the tribute.",
                        $"Deployment succeeded for {repo}. Nobody touch anything.",
                        $"{repo} is live, Jed. The code escaped and appears stable.",
                        $"Successful deployment on {repo}. Even production seems surprised.",
                        $"Jed, {repo} made it through deployment intact. Buy a lottery ticket.");
                }
                else if (deploymentState is "failure" or "error")
                {
                    type = "github.deployment.failed";
                    severity = "critical";
                    emotion = "urgent";
                    message = Pick(random,
                        $"Jed, deployment failed for {repo}. Production rejected the organ transplant.",
                        $"{repo} failed to deploy. The launch pad is mostly smoke now.",
                        $"Deployment failure on {repo}, Jed. The code has been denied entry.",
                        $"Jed, {repo} tripped on the way to production and took the pipeline with it.",
                        $"The deployment for {repo} has gone spectacularly sideways.");
                }
                break;

            case "repository_vulnerability_alert":
                type = "github.security_alert";
                severity = "critical";
                emotion = "urgent";
                message = Pick(random,
                    $"Jed, GitHub found a security problem in {repo}. The gremlins have credentials now.",
                    $"Security alert for {repo}. Something shady has crawled out of the dependency tree.",
                    $"Jed, {repo} has a vulnerability alert. Please bring a patch and a flamethrower.",
                    $"GitHub is waving a red flag at {repo}. This one is not decorative.",
                    $"Security trouble in {repo}, Jed. One of the dependencies has chosen villainy.");
                break;
        }

        return type is null ? null : new("github", type, severity, emotion, message, null,
            string.IsNullOrWhiteSpace(delivery) ? $"github:{name}:{Guid.NewGuid()}" : $"github:{delivery}",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(6), null, null, null);
    }

    private static string BuildFailed(Random random, string repo, string workflow) => Pick(random,
        $"Jed, the build has shit the bed on {repo}. {workflow} is the culprit.",
        $"Jed, {workflow} failed on {repo}. The build server has chosen violence.",
        $"Bad news from {repo}: {workflow} fell down the stairs and blamed the compiler.",
        $"Jed, the {repo} build is broken. {workflow} has converted code into sadness.",
        $"{workflow} failed on {repo}. Somewhere, a semicolon is feeling smug.",
        $"Jed, {repo} did not build. The computers reviewed the code and said absolutely not.",
        $"Build failure on {repo}. {workflow} has discovered a creative new definition of done.",
        $"Jed, {workflow} exploded on {repo}. The logs are now a crime scene.",
        $"{repo} has a failed build. Please collect your stack trace from the floor.",
        $"Jed, {workflow} failed in {repo}. The pipeline is making that expensive grinding noise again.",
        $"The build on {repo} is toast. {workflow} supplied the toaster and the fork.",
        $"Jed, {repo} has achieved negative compilation. {workflow} sends its regrets.",
        $"{workflow} failed on {repo}. The code and reality remain in active disagreement.",
        $"Jed, the build for {repo} has gone pear-shaped, then caught fire.",
        $"Failure in {repo}: {workflow} stepped on a rake it installed itself.",
        $"Jed, {workflow} broke on {repo}. Even the error message sounds disappointed.",
        $"The {repo} build failed. Apparently hope is not a valid dependency.",
        $"Jed, {workflow} has face-planted in {repo}. Ten out of ten for commitment.",
        $"Build down on {repo}. {workflow} is currently hiding behind the logs.",
        $"Jed, {repo} failed to build. The bugs have unionized and seized the pipeline.");

    private static string BuildSucceeded(Random random, string repo, string workflow) => Pick(random,
        $"I don't believe it. We've got a successful build on {repo}. Someone else must have pushed code for you.",
        $"Jed, {workflow} passed on {repo}. Against all odds, the computers behaved.",
        $"Successful build on {repo}. Nobody move; we may frighten it.",
        $"Jed, {repo} built successfully. I checked twice because that sounded made up.",
        $"{workflow} passed on {repo}. The compiler has temporarily withdrawn its objections.",
        $"Jed, the {repo} build is green. Take a picture before it changes its mind.",
        $"Build success on {repo}. The pipeline has produced something other than emotional damage.",
        $"Jed, {workflow} succeeded in {repo}. Even a stopped clock compiles twice a day.",
        $"{repo} has a successful build. The prophecy was true.",
        $"Jed, the tests passed and {repo} built. Clearly the machines want something.",
        $"{workflow} is green on {repo}. Suspicious, but technically good news.",
        $"Jed, {repo} compiled without drama. I hardly recognized it.",
        $"Successful build for {repo}. The bugs are simply waiting for production now.",
        $"Jed, {workflow} passed on {repo}. Please enjoy this brief administrative miracle.",
        $"The {repo} build succeeded. Somewhere, a flaky test missed its cue.",
        $"Jed, all green on {repo}. The dashboard is clearly showing off.",
        $"{workflow} succeeded in {repo}. Competence has been detected.",
        $"Jed, {repo} built cleanly. We will be investigating this anomaly.",
        $"The build passed on {repo}. Put the champagne back; we still have deployment.",
        $"Jed, {workflow} is successful on {repo}. Fine work, whoever actually wrote it.");

    private static string Pick(Random random, params string[] messages) => messages[random.Next(messages.Length)];

    private static string? TryString(JsonElement root, params string[] path)
    {
        foreach (var part in path)
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(part, out root)) return null;
        return root.ValueKind == JsonValueKind.String ? root.GetString() : null;
    }

    private static int? TryInt(JsonElement root, params string[] path)
    {
        foreach (var part in path)
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(part, out root)) return null;
        return root.TryGetInt32(out var value) ? value : null;
    }

    private static bool? TryBool(JsonElement root, params string[] path)
    {
        foreach (var part in path)
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(part, out root)) return null;
        return root.ValueKind is JsonValueKind.True or JsonValueKind.False ? root.GetBoolean() : null;
    }
}
