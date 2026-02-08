using AgentSandbox.InteractiveAgent;
using AgentSandbox.Core;
using AgentSandbox.Core.Shell;
using AgentSandbox.Core.Shell.Extensions;
using AgentSandbox.Core.Skills;
using AgentSandbox.Core.Telemetry;
using AgentSandbox.Extensions.SemanticKernel;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI.Chat;

var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
var deployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT");

if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(deployment))
{
    Console.WriteLine("Set AZURE_OPENAI_ENDPOINT and AZURE_OPENAI_DEPLOYMENT to run this sample.");
    return;
}

// Sandbox setup
// Get the skills folder path relative to the executable
var skillsPath = Path.Combine(AppContext.BaseDirectory, "Skills");

using var sandbox = new Sandbox(options: new SandboxOptions
{
    ShellExtensions = new List<IShellCommand>
    {
        new GitCommand()
    },
    AgentSkills = new AgentSkillOptions
    {
        Skills = 
        [
            AgentSkill.FromPath(Path.Combine(skillsPath, "brainstorming")),
            AgentSkill.FromPath(Path.Combine(skillsPath, "executing-plans"))
        ]
    },
    Telemetry = new SandboxTelemetryOptions
    {
        Enabled = true
    }
});

// Subscribe console telemetry observer for real-time event monitoring
var consoleObserver = new ConsoleTelemetryObserver(
    new ConsoleTelemetryOptions
    {
        ShowCommands = true,
        ShowCommandDetails = true,
        ShowFileChanges = true,
        ShowSkills = true,
        ShowLifecycle = true,
        ShowErrors = true
    });

using var subscription = sandbox.Subscribe(consoleObserver);

// AI Agent setup
var chatClient = new AzureOpenAIClient(new Uri(endpoint), new AzureCliCredential())
    .GetChatClient(deployment);


AIAgent agent = chatClient.AsAIAgent(
    instructions: "You are a helpful assistant.",
    name: "SandboxAgent",
    tools: new[] { KernelExtensions.GetBashFunction(sandbox), KernelExtensions.CreateGetSkillFunction(sandbox) })
    .AsBuilder()
    .Build();

AgentThread agentThread = await agent.GetNewThreadAsync();

Console.WriteLine("=== AgentSandbox Agent Playground ===");
Console.WriteLine("Telemetry monitoring enabled. Type 'exit' to quit.\n");

while (true)
{
    Console.Write("Agent > ");

    var input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input))
        continue;

    if (input.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase))
        break;

    var response = await agent.RunAsync(input, agentThread);

    Console.WriteLine(response.Text);
}
