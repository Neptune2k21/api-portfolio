using System.Text.Json;
using System.Text.Json.Serialization;

namespace Portfolio.Api.Features.Articles.Sources;

internal sealed record GraphQlRequest(string Query, Dictionary<string, string> Variables);

internal sealed record GraphQlResponse<T>(T? Data, IReadOnlyList<GraphQlError>? Errors)
    where T : class;

internal sealed record GraphQlError(string Message);

internal sealed record TreeData(TreeRepository? Repository);

internal sealed record TreeRepository(GitObject? Object);

/// <summary>Un dossier (<see cref="Entries"/>) ou un fichier (<see cref="Text"/>, null s'il est binaire).</summary>
internal sealed record GitObject(IReadOnlyList<TreeEntry>? Entries, string? Text, bool IsTruncated);

internal sealed record TreeEntry(string Path, string Type, GitObject? Object);

internal sealed record HistoryData(HistoryRepository? Repository);

internal sealed record HistoryRepository(HistoryRef? Ref);

/// <summary>Un historique par fichier demandé, sous les alias f0, f1…</summary>
internal sealed record HistoryRef(Dictionary<string, CommitHistory>? Target);

internal sealed record CommitHistory(IReadOnlyList<CommitNode> Nodes);

internal sealed record CommitNode(string Oid, DateTimeOffset AuthoredDate, string MessageHeadline, string Url);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(GraphQlRequest))]
[JsonSerializable(typeof(GraphQlResponse<TreeData>))]
[JsonSerializable(typeof(GraphQlResponse<HistoryData>))]
internal sealed partial class GitHubJson : JsonSerializerContext;
