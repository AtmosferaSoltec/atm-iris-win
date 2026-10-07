using System;
using System.Collections.Generic;

namespace Iris.Core.Networking.Dto;

// Church, people, service types, songs, media, records, sync and Bible (api-contract §6–14).

public sealed record ChurchModulesDto(bool Bible, bool Multimedia, bool TimeControl);

/// <summary>api-contract §6: typeface, size (referred to a 1920-wide screen) and the background shown
/// when none is chosen. <see cref="FontFamily"/> is one of 10 keys, never a font name.</summary>
public sealed record ProjectionSettingsDto(string FontFamily, int FontSizePt, string? DefaultBackgroundId);

/// <summary>api-contract §6: one quota per church, shared by Música, Fondos and Multimedia. <see cref="Breakdown"/>
/// is nullable only so a copy saved before it existed still decodes.</summary>
public sealed record StorageDto(long UsedBytes, long QuotaBytes, StorageBreakdownDto? Breakdown = null);

/// <summary>What each web section takes: audio; images and videos marked as background; the rest.</summary>
public sealed record StorageBreakdownDto(long MusicBytes, long BackgroundBytes, long MediaBytes);

public sealed record ChurchDto(
    Guid Id,
    string Name,
    string Timezone,
    ChurchModulesDto Modules,
    StorageDto Storage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    // Nullable only so a reply from an older server still decodes; `Mapping` fills the defaults.
    ChurchModulesDto? AvailableModules = null,
    ProjectionSettingsDto? Projection = null);

public sealed record ChurchPatchDto(string? Name, string? Timezone);

public sealed record PersonDto(Guid Id, string Name, int BlockCount, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record PersonCreateDto(Guid? Id, string Name);

public sealed record PersonRenameDto(string Name);

public sealed record ScheduleDto(int Weekday, int Hour, int Minute);

// api-contract §9: no responsible person on the template anymore — it rotates weekly and is
// recorded on each service instead.
public sealed record BlockTemplateDto(Guid Id, string Name, int PlannedMinutes);

public sealed record BlockTemplateInputDto(Guid? Id, string Name, int PlannedMinutes);

public sealed record ServiceTypeDto(
    Guid Id,
    string Name,
    string Color,
    ScheduleDto? Schedule,
    IReadOnlyList<BlockTemplateDto> Blocks,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ServiceTypeInputDto(string Name, string Color, ScheduleDto? Schedule, IReadOnlyList<BlockTemplateInputDto> Blocks);

public sealed record ServiceTypeCreateDto(Guid? Id, string Name, string Color, ScheduleDto? Schedule, IReadOnlyList<BlockTemplateInputDto> Blocks);

public sealed record SongSectionDto(Guid Id, string? Label, string Text);

public sealed record SongDto(
    Guid Id,
    string Title,
    string Author,
    IReadOnlyList<SongSectionDto> Sections,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record SongSummaryDto(Guid Id, string Title, string Author, int SectionCount, string? FirstLine, DateTimeOffset UpdatedAt);

public sealed record MediaAssetDto(
    Guid Id,
    string Kind,
    string Title,
    string? Description,
    string FileName,
    string ContentType,
    long SizeBytes,
    double? DurationSeconds,
    int? Width,
    int? Height,
    bool IsBackground,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record DownloadUrlDto(string Url, DateTimeOffset ExpiresAt);

public sealed record BlockRecordDto(
    Guid Id,
    string Name,
    int PlannedSeconds,
    int ActualSeconds,
    Guid? PersonId,
    string? PersonName,
    string Status);

public sealed record ServiceRecordDto(
    Guid Id,
    DateTimeOffset Date,
    Guid ServiceTypeId,
    string ServiceTypeName,
    IReadOnlyList<BlockRecordDto> Blocks,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ServiceRecordInputDto(DateTimeOffset Date, Guid ServiceTypeId, string ServiceTypeName, IReadOnlyList<BlockRecordDto> Blocks);

public sealed record BlockActualPatchDto(int ActualSeconds);

public sealed record BlockPersonPatchDto(Guid? PersonId);

public sealed record SyncChangesDto(
    IReadOnlyList<PersonDto> People,
    IReadOnlyList<ServiceTypeDto> ServiceTypes,
    IReadOnlyList<SongDto> Songs,
    IReadOnlyList<MediaAssetDto> Media,
    IReadOnlyList<ServiceRecordDto> ServiceRecords);

public sealed record SyncDeletedDto(
    IReadOnlyList<Guid> People,
    IReadOnlyList<Guid> ServiceTypes,
    IReadOnlyList<Guid> Songs,
    IReadOnlyList<Guid> Media,
    IReadOnlyList<Guid> ServiceRecords);

public sealed record SyncPageDto(ChurchDto? Church, SyncChangesDto Changes, SyncDeletedDto Deleted, string Cursor, bool HasMore);

public sealed record BibleTranslationDto(string Code, string Name, string Language, int Version, long SizeBytes);

public sealed record BibleBookDto(string Id, string Name, string Testament, int ChapterCount, int Position);

public sealed record BibleBookDownloadDto(
    string Id,
    string Name,
    string Testament,
    int ChapterCount,
    int Position,
    IReadOnlyList<IReadOnlyList<string>> Chapters);

public sealed record BibleDownloadDto(string Code, string Name, int Version, IReadOnlyList<BibleBookDownloadDto> Books);

public sealed record BibleVerseDto(int Number, string Text);

public sealed record BibleChapterDto(string BookId, int Chapter, IReadOnlyList<BibleVerseDto> Verses);

public sealed record SongSectionInputDto(string? Label, string Text);

public sealed record SongInputDto(string Title, string Author, IReadOnlyList<SongSectionInputDto> Sections);

public sealed record SongCreateDto(Guid? Id, string Title, string Author, IReadOnlyList<SongSectionInputDto> Sections);

public sealed record MediaUploadRequestDto(string Kind, string FileName, string ContentType, long SizeBytes);

public sealed record UploadTicketDto(Guid UploadId, string UploadUrl, IReadOnlyDictionary<string, string> Headers, DateTimeOffset ExpiresAt);

public sealed record MediaConfirmDto(Guid UploadId, string Title, string? Description, double? DurationSeconds, int? Width, int? Height, bool? IsBackground);

public sealed record MediaPatchDto(string? Title, string? Description, bool? IsBackground);

/// <summary>The <c>{ data }</c> envelope of the Bible download, so it can be read as a stream.</summary>
public sealed record BibleEnvelopeDto(BibleDownloadDto Data);

/// <summary>What the console remembers about its Bible file (<c>rvr1909.meta.json</c>).</summary>
public sealed record BibleMetaDto(int Version, string? ETag, DateTimeOffset CheckedAt);
