using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace TestLens.Core.Tests.Helpers;

public static class TestWorkspaceHelper
{
    public static (Solution Solution, Project Project, Document Document) CreateWorkspaceFromSource(
        string sourceCode,
        string assemblyName = "TestAssembly")
    {
        var projectId = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId);

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location))
            .ToList();

        var projectInfo = ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            assemblyName,
            assemblyName,
            LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            parseOptions: new CSharpParseOptions(LanguageVersion.Latest),
            metadataReferences: references);

        var workspace = new AdhocWorkspace();
        var newSolution = workspace.CurrentSolution
            .AddProject(projectInfo)
            .AddDocument(documentId, "Source.cs", SourceText.From(sourceCode));

        var project = newSolution.GetProject(projectId)!;
        var document = project.GetDocument(documentId)!;

        return (newSolution, project, document);
    }
}
