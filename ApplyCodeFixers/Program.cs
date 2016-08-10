using AbbreviationFix;
using StyleCop.Analyzers.SpacingRules;

namespace StyleCopTester
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Diagnostics;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Windows.Threading;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CodeActions;
    using Microsoft.CodeAnalysis.CodeFixes;
    using Microsoft.CodeAnalysis.Diagnostics;
    using Microsoft.CodeAnalysis.MSBuild;
    using StyleCop.Analyzers;

    internal class FixerTask
    {
        public FixerTask(DiagnosticAnalyzer analyzer, CodeFixProvider fixProvider)
        {
            Analyzer = analyzer;
            FixProvider = fixProvider;
        }

        public DiagnosticAnalyzer Analyzer { get; }

        public CodeFixProvider FixProvider { get; }
    }

    internal class AppConfig
    {
        public bool CheckOnlyMode { get; private set; }

        public string SolutionPath { get; private set; }

        public void Initialize(string[] args)
        {
            CheckOnlyMode = args.Contains("/check");

            SolutionPath = args.SingleOrDefault(i => !i.StartsWith("/", StringComparison.Ordinal));

            var configParamPrefix = "/config:";
            var configParam = args.SingleOrDefault(i => i.StartsWith(configParamPrefix));
            if (configParam != null)
            {
                var configFile = configParam.Substring(configParamPrefix.Length);
                ConfigLoader.Load(configFile);
            }
        }
    }

    /// <summary>
    /// StyleCopTester is a tool that will analyze a solution, find diagnostics in it and will print out the number of
    /// diagnostics it could find. This is useful to easily test performance without having the overhead of visual
    /// studio running.
    /// </summary>
    internal static class Program
    {
        private static AppConfig appConfig = new AppConfig();

        private static void Main(string[] args)
        {
            Version roslynVersion = typeof(AdditionalText).GetTypeInfo().Assembly.GetName().Version;
            bool roslynVersion110 = roslynVersion >= new Version(1, 1, 0, 0) && roslynVersion < new Version(1, 2, 0, 0);
            if (roslynVersion110)
            {
                throw new ApplicationException("Version of Roslyn is not supported");
            }

            appConfig.Initialize(args);

            CancellationTokenSource cts = new CancellationTokenSource();
            Console.CancelKeyPress +=
                (sender, e) =>
                {
                    e.Cancel = true;
                    cts.Cancel();
                };

            // Since Console apps do not have a SynchronizationContext, we're leveraging the built-in support
            // in WPF to pump the messages via the Dispatcher.
            // See the following for additional details:
            //   http://blogs.msdn.com/b/pfxteam/archive/2012/01/21/10259307.aspx
            //   https://github.com/DotNetAnalyzers/StyleCopAnalyzers/pull/1362
            SynchronizationContext previousContext = SynchronizationContext.Current;
            try
            {
                var context = new DispatcherSynchronizationContext();
                SynchronizationContext.SetSynchronizationContext(context);

                DispatcherFrame dispatcherFrame = new DispatcherFrame();
                Task mainTask = MainAsync(args, cts.Token);
                mainTask.ContinueWith(task => dispatcherFrame.Continue = false);

                Dispatcher.PushFrame(dispatcherFrame);
                mainTask.GetAwaiter().GetResult();
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }
        }

        private static async Task MainAsync(string[] args, CancellationToken cancellationToken)
        {
            // A valid call must have at least one parameter (a solution file). Optionally it can include /all or /id:SAXXXX.
            if (args.Length < 1)
            {
                PrintHelp();
                return;
            }

            var tasks = GetFixerTasks();
            if (tasks.Count == 0)
            {
                Console.WriteLine("Empty analyzer/fixer list");
                return;
            }

            Stopwatch stopwatch = Stopwatch.StartNew();

            if (appConfig.CheckOnlyMode)
            {
                var solution = await LoadSolution(appConfig.SolutionPath, cancellationToken, stopwatch);
                stopwatch.Restart();

                List<Project> csharpProjects =
                    solution.Projects.Where(i => i.Language == LanguageNames.CSharp).ToList();

                Console.WriteLine("Number of projects:\t\t" + csharpProjects.Count);
                Console.WriteLine("Number of documents:\t\t" + csharpProjects.Sum(x => x.DocumentIds.Count));

                var statistics =
                    await GetAnalyzerStatisticsAsync(csharpProjects, cancellationToken).ConfigureAwait(true);

                Console.WriteLine("Number of syntax nodes:\t\t" + statistics.NumberofNodes);
                Console.WriteLine("Number of syntax tokens:\t" + statistics.NumberOfTokens);
                Console.WriteLine("Number of syntax trivia:\t" + statistics.NumberOfTrivia);

                var diagnostics =
                    await
                        GetAnalyzerDiagnosticsAsync(solution, tasks.Select(x => x.Analyzer).ToImmutableArray(), false, cancellationToken)
                            .ConfigureAwait(true);
                var allDiagnostics = diagnostics.SelectMany(i => i.Value).ToImmutableArray();

                Console.WriteLine($"Found {allDiagnostics.Length} diagnostics in {stopwatch.ElapsedMilliseconds}ms");

                foreach (
                    var group in
                        allDiagnostics.GroupBy(i => i.Id).OrderBy(i => i.Key, StringComparer.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"  {group.Key}: {group.Count()} instances");
                    // Print out analyzer diagnostics like AD0001 for analyzer exceptions
                    if (group.Key.StartsWith("AD", StringComparison.Ordinal))
                    {
                        foreach (var item in group)
                        {
                            Console.WriteLine(item);
                        }
                    }
                }

                // TODO: Implement quick check: Exit with code 1 when first violation found
                if (allDiagnostics.Any())
                {
                    Environment.Exit(1);
                }

                Environment.Exit(0);
            }

            await ApplyFixers(tasks, cancellationToken, stopwatch).ConfigureAwait(true);

            Console.ReadKey();
        }

        private static async Task<Solution> LoadSolution(string solutionPath, CancellationToken cancellationToken, Stopwatch stopwatch)
        {
            MSBuildWorkspace workspace = MSBuildWorkspace.Create();
            var solution = await workspace.OpenSolutionAsync(solutionPath, cancellationToken).ConfigureAwait(false);
            Console.WriteLine($"Loaded solution in {stopwatch.ElapsedMilliseconds}ms");
            return solution;
        }

        private static async Task ApplyFixers(
            List<FixerTask> fixerTasks,
            CancellationToken cancellationToken,
            Stopwatch stopwatch)
        {
            Console.WriteLine("Calculating fixes");

            var taskIndex = 0;
            var fixerTask = fixerTasks[taskIndex];
            Action scheduleNextTask = () => taskIndex++;

            while (taskIndex != fixerTasks.Count)
            {
                // TODO: Check analyzer - fixer compatibility
                var solution = await LoadSolution(appConfig.SolutionPath, cancellationToken, stopwatch);
                stopwatch.Restart();

                var diagnostics =
                    await
                        GetAnalyzerDiagnosticsAsync(solution, new[] { fixerTask.Analyzer }.ToImmutableArray(), false, cancellationToken)
                            .ConfigureAwait(true);

                var equivalenceGroups = new List<CodeFixEquivalenceGroup>();
                equivalenceGroups.AddRange(await CodeFixEquivalenceGroup.CreateAsync(fixerTask.FixProvider, diagnostics, solution, cancellationToken).ConfigureAwait(true));

                if (equivalenceGroups.Count == 0)
                {
                    scheduleNextTask();
                    continue;
                }

                var fix = equivalenceGroups[0];
                try
                {
                    stopwatch.Restart();
                    WriteLine($"Calculating fix for {fix.CodeFixEquivalenceKey} using {fix.FixAllProvider} for {fix.NumberOfDiagnostics} instances.");
                    var operations = await fix.GetOperationsAsync(cancellationToken).ConfigureAwait(true);
                    var applyOperations = operations.OfType<ApplyChangesOperation>().ToList();
                    if (applyOperations.Count > 1)
                    {
                        // TODO: Consider ot reload solution after applying each single code action.
                        // TODO: Need to collect info which Fixers have multiple code actions operations.
                        WriteLine("Only a single code action operations are supported.", ConsoleColor.Red);
                        scheduleNextTask();
                        continue;
                    }

                    if (applyOperations.Count == 0)
                    {
                        WriteLine("No changes were found to apply.", ConsoleColor.Yellow);
                    }
                    else
                    {
                        applyOperations[0].Apply(solution.Workspace, cancellationToken);
                    }

                    WriteLine(
                        $"Calculating changes completed in {stopwatch.ElapsedMilliseconds}ms. This is {fix.NumberOfDiagnostics / stopwatch.Elapsed.TotalSeconds:0.000} instances/second.", ConsoleColor.Yellow);
                }
                catch (Exception ex)
                {
                    // Report thrown exceptions
                    WriteLine($"The fix '{fix.CodeFixEquivalenceKey}' threw an exception after {stopwatch.ElapsedMilliseconds}ms:", ConsoleColor.Red);
                    WriteLine(ex.ToString(), ConsoleColor.Yellow);
                    return;
                }

                if (equivalenceGroups.Count > 1)
                {
                    // TODO: Need to collect info about Fixers that have actions with different eqvivalence keys. Please report about these cases
                    WriteLine("Multiple equvivalnce groups per fixer may lead to problems. Please report this case", ConsoleColor.Yellow);
                    foreach (var codeFixEquivalenceGroup in equivalenceGroups)
                    {
                        WriteLine(string.Join(", ", codeFixEquivalenceGroup.CodeFixProvider.FixableDiagnosticIds) + "/" + 
                        codeFixEquivalenceGroup.CodeFixEquivalenceKey, ConsoleColor.Yellow);
                    }

                    // Reload solution and apply same fixer one more time. It is assumed that count of equivalence groups
                    // will be reduced during iterations.. Otherwise application will stuck
                    continue;
                }
                scheduleNextTask();
            }
        }

        private static void WriteLine(string text, ConsoleColor color = ConsoleColor.White)
        {
            Console.ForegroundColor = color;
            Console.WriteLine(text);
            Console.ResetColor();
        }

        private static Task<Statistic> GetAnalyzerStatisticsAsync(IEnumerable<Project> projects, CancellationToken cancellationToken)
        {
            ConcurrentBag<Statistic> sums = new ConcurrentBag<Statistic>();

            Parallel.ForEach(projects.SelectMany(i => i.Documents), document =>
            {
                var documentStatistics = GetAnalyzerStatisticsAsync(document, cancellationToken).ConfigureAwait(false).GetAwaiter().GetResult();
                sums.Add(documentStatistics);
            });

            Statistic sum = sums.Aggregate(new Statistic(0, 0, 0), (currentResult, value) => currentResult + value);
            return Task.FromResult(sum);
        }

        private static async Task<Statistic> GetAnalyzerStatisticsAsync(Document document, CancellationToken cancellationToken)
        {
            SyntaxTree tree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);

            SyntaxNode root = await tree.GetRootAsync(cancellationToken).ConfigureAwait(false);

            var tokensAndNodes = root.DescendantNodesAndTokensAndSelf(descendIntoTrivia: true);

            int numberOfNodes = tokensAndNodes.Count(x => x.IsNode);
            int numberOfTokens = tokensAndNodes.Count(x => x.IsToken);
            int numberOfTrivia = root.DescendantTrivia(descendIntoTrivia: true).Count();

            return new Statistic(numberOfNodes, numberOfTokens, numberOfTrivia);
        }

        private static List<FixerTask> GetFixerTasks()
        {
            var ret = new List<FixerTask>();
            ret.Add(new FixerTask(new AbbreviationFixAnalyzer(), new AbbreviationFixCodeFixProvider()));
            //ret.Add(new FixerTask(new SA1003SymbolsMustBeSpacedCorrectly(), new SA1003CodeFixProvider()));

            return ret;
        }

        private static async Task<ImmutableDictionary<ProjectId, ImmutableArray<Diagnostic>>> GetAnalyzerDiagnosticsAsync(Solution solution, ImmutableArray<DiagnosticAnalyzer> analyzers, bool force, CancellationToken cancellationToken)
        {
            List<KeyValuePair<ProjectId, Task<ImmutableArray<Diagnostic>>>> projectDiagnosticTasks = new List<KeyValuePair<ProjectId, Task<ImmutableArray<Diagnostic>>>>();

            // Make sure we analyze the projects in parallel
            foreach (var project in solution.Projects)
            {
                if (project.Language != LanguageNames.CSharp)
                {
                    continue;
                }

                projectDiagnosticTasks.Add(new KeyValuePair<ProjectId, Task<ImmutableArray<Diagnostic>>>(project.Id, GetProjectAnalyzerDiagnosticsAsync(analyzers, project, force, cancellationToken)));
            }

            ImmutableDictionary<ProjectId, ImmutableArray<Diagnostic>>.Builder projectDiagnosticBuilder = ImmutableDictionary.CreateBuilder<ProjectId, ImmutableArray<Diagnostic>>();
            foreach (var task in projectDiagnosticTasks)
            {
                projectDiagnosticBuilder.Add(task.Key, await task.Value.ConfigureAwait(false));
            }

            return projectDiagnosticBuilder.ToImmutable();
        }

        /// <summary>
        /// Returns a list of all analyzer diagnostics inside the specific project. This is an asynchronous operation.
        /// </summary>
        /// <param name="analyzers">The list of analyzers that should be used</param>
        /// <param name="project">The project that should be analyzed</param>
        /// <param name="force"><see langword="true"/> to force the analyzers to be enabled; otherwise,
        /// <see langword="false"/> to use the behavior configured for the specified <paramref name="project"/>.</param>
        /// <param name="cancellationToken">The cancellation token that the task will observe.</param>
        /// <returns>A list of diagnostics inside the project</returns>
        private static async Task<ImmutableArray<Diagnostic>> GetProjectAnalyzerDiagnosticsAsync(ImmutableArray<DiagnosticAnalyzer> analyzers, Project project, bool force, CancellationToken cancellationToken)
        {
            var supportedDiagnosticsSpecificOptions = new Dictionary<string, ReportDiagnostic>();
            if (force)
            {
                foreach (var analyzer in analyzers)
                {
                    foreach (var diagnostic in analyzer.SupportedDiagnostics)
                    {
                        // make sure the analyzers we are testing are enabled
                        supportedDiagnosticsSpecificOptions[diagnostic.Id] = ReportDiagnostic.Default;
                    }
                }
            }

            // Report exceptions during the analysis process as errors
            supportedDiagnosticsSpecificOptions.Add("AD0001", ReportDiagnostic.Error);

            // update the project compilation options
            var modifiedSpecificDiagnosticOptions = supportedDiagnosticsSpecificOptions.ToImmutableDictionary().SetItems(project.CompilationOptions.SpecificDiagnosticOptions);
            var modifiedCompilationOptions = project.CompilationOptions.WithSpecificDiagnosticOptions(modifiedSpecificDiagnosticOptions);
            var processedProject = project.WithCompilationOptions(modifiedCompilationOptions);

            Compilation compilation = await processedProject.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            CompilationWithAnalyzers compilationWithAnalyzers = compilation.WithAnalyzers(analyzers, cancellationToken: cancellationToken);

            // In everything except Roslyn 1.1, we use GetAllDiagnosticsAsync and return the result.
            var diagnostics = await compilationWithAnalyzers.GetAllDiagnosticsAsync().ConfigureAwait(false);
            return diagnostics;
        }

        private static void PrintHelp()
        {
            Console.WriteLine("Usage: ApplyCodeFixer [options] <Solution>");
            Console.WriteLine("Options:");
            Console.WriteLine("/check       Do not fix problems. Exit code = 1 in case if there is something to fix, exitcode = 0 otherwise");
        }
    }
}
