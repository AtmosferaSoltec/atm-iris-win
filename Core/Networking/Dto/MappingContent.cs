using System;
using System.Collections.Generic;
using System.Linq;
using Iris.Core.Models;

namespace Iris.Core.Networking.Dto;

public static partial class Mapping
{
    // ----- People -----

    public static Person ToModel(PersonDto dto) => new(dto.Id, dto.Name, dto.BlockCount);

    public static PersonDto ToDto(Person person, DateTimeOffset now) => new(person.Id, person.Name, person.BlockCount, now, now);

    // ----- Service types -----

    public static ServiceType ToModel(ServiceTypeDto dto) => new(
        dto.Id,
        dto.Name,
        dto.Color,
        dto.Schedule is { } s ? new ServiceSchedule(s.Weekday, s.Hour, s.Minute) : null,
        dto.Blocks.Select(b => new BlockTemplate(b.Id, b.Name, b.PlannedMinutes, b.DefaultPersonId)).ToList());

    public static ServiceTypeDto ToDto(ServiceType type, DateTimeOffset createdAt, DateTimeOffset updatedAt) => new(
        type.Id,
        type.Name,
        type.Color,
        type.Schedule is { } s ? new ScheduleDto(s.Weekday, s.Hour, s.Minute) : null,
        type.Blocks.Select(b => new BlockTemplateDto(b.Id, b.Name, b.PlannedMinutes, b.DefaultPersonId)).ToList(),
        createdAt,
        updatedAt);

    public static ServiceTypeInputDto ToInput(ServiceType type) => new(
        type.Name,
        type.Color,
        type.Schedule is { } s ? new ScheduleDto(s.Weekday, s.Hour, s.Minute) : null,
        type.Blocks.Select(b => new BlockTemplateInputDto(b.Id, b.Name, b.PlannedMinutes, b.DefaultPersonId)).ToList());

    // ----- Songs -----

    public static LyricSheet ToModel(SongDto dto) => new(
        dto.Id,
        dto.Title,
        dto.Author,
        dto.Sections.Select(s => new Slide(s.Id, s.Label, new TextContent(s.Text))).ToList(),
        string.IsNullOrWhiteSpace(dto.Copyright) ? null : dto.Copyright);

    public static SongDto ToDto(LyricSheet sheet, DateTimeOffset now) => new(
        sheet.Id,
        sheet.Title,
        sheet.Author,
        sheet.Copyright,
        sheet.Sections
            .Select(s => new SongSectionDto(s.Id, s.Label, (s.Content as TextContent)?.Body ?? string.Empty))
            .ToList(),
        now,
        now);

    // ----- Time records (dates are church-local in the app, UTC on the wire) -----

    public static BlockStatus ParseStatus(string? value) => value switch
    {
        "skipped" => BlockStatus.Skipped,
        "adjusted" => BlockStatus.Adjusted,
        _ => BlockStatus.Completed,
    };

    public static string ToWire(BlockStatus status) => status switch
    {
        BlockStatus.Skipped => "skipped",
        BlockStatus.Adjusted => "adjusted",
        _ => "completed",
    };

    public static ServiceRecord ToModel(ServiceRecordDto dto, TimeZoneInfo zone) => new(
        dto.Id,
        DateTime.SpecifyKind(TimeZoneInfo.ConvertTime(dto.Date, zone).DateTime, DateTimeKind.Unspecified),
        dto.ServiceTypeId,
        dto.Blocks
            .Select(b => new BlockRecord(b.Id, b.Name, b.PlannedSeconds, b.ActualSeconds, b.PersonId, b.PersonName, ParseStatus(b.Status)))
            .ToList(),
        dto.ServiceTypeName);

    public static ServiceRecordDto ToDto(ServiceRecord record, TimeZoneInfo zone, DateTimeOffset createdAt, DateTimeOffset updatedAt) => new(
        record.Id,
        ToUtc(record.Date, zone),
        record.ServiceTypeId,
        record.ServiceTypeName,
        ToBlockDtos(record),
        createdAt,
        updatedAt);

    /// <summary>The body of <c>PUT /service-records/:id</c>: blocks go as completed or skipped (adjusting is a separate PATCH).</summary>
    public static ServiceRecordInputDto ToInput(ServiceRecord record, TimeZoneInfo zone) => new(
        ToUtc(record.Date, zone),
        record.ServiceTypeId,
        record.ServiceTypeName,
        record.Blocks
            .Select(b => new BlockRecordDto(
                b.Id,
                b.Name,
                b.PlannedSeconds,
                b.Status == BlockStatus.Skipped ? 0 : b.ActualSeconds,
                b.PersonId,
                b.PersonName,
                b.Status == BlockStatus.Skipped ? "skipped" : "completed"))
            .ToList());

    public static DateTimeOffset ToUtc(DateTime local, TimeZoneInfo zone) => new ChurchClock(zone).ToUtc(local);

    private static List<BlockRecordDto> ToBlockDtos(ServiceRecord record) => record.Blocks
        .Select(b => new BlockRecordDto(b.Id, b.Name, b.PlannedSeconds, b.ActualSeconds, b.PersonId, b.PersonName, ToWire(b.Status)))
        .ToList();
}


public static partial class Mapping
{
    // ----- Media -----

    public static string ToWire(MediaKind kind) => kind switch
    {
        MediaKind.Music => "audio",
        MediaKind.Video => "video",
        _ => "image",
    };

    public static MediaKind ParseKind(string? value) => value switch
    {
        "audio" => MediaKind.Music,
        "video" => MediaKind.Video,
        _ => MediaKind.Image,
    };

    /// <summary>A library entry with the state of its cached file (nothing to show until it is on this PC).</summary>
    public static MediaAsset ToModel(MediaAssetDto dto, Iris.Core.Media.MediaFileState state)
    {
        var kind = ParseKind(dto.Kind);
        return new MediaAsset(
            dto.Id,
            kind,
            dto.Title,
            dto.Description ?? string.Empty,
            dto.DurationSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null,
            kind switch
            {
                MediaKind.Music => ["#0F2417", "#3DDC97"],
                MediaKind.Video => ["#2A1658", "#4E5BFF"],
                _ => ["#15151F", "#07070B"],
            })
        {
            LocalPath = state.Availability == MediaAvailability.Ready ? state.Path : null,
            Availability = state.Availability,
            Progress = state.Progress,
            Width = dto.Width,
            Height = dto.Height,
            IsBackground = dto.IsBackground,
        };
    }
}
