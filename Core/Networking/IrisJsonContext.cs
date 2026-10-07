using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Iris.Core.Networking.Dto;

namespace Iris.Core.Networking;

/// <summary>ISO-8601 UTC with milliseconds ("2026-10-05T15:30:31.022Z"), as the contract requires.</summary>
public sealed class IsoDateConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        DateTimeOffset.Parse(reader.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).ToUniversalTime();

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
}

/// <summary>Generated serializer (no reflection: Release trims the app). Explicit nulls, camelCase.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    Converters = [typeof(IsoDateConverter)])]
[JsonSerializable(typeof(UserDto))]
[JsonSerializable(typeof(SessionViewDto))]
[JsonSerializable(typeof(AuthResultDto))]
[JsonSerializable(typeof(SignInBodyDto))]
[JsonSerializable(typeof(SignUpBodyDto))]
[JsonSerializable(typeof(RefreshBodyDto))]
[JsonSerializable(typeof(EmailBodyDto))]
[JsonSerializable(typeof(VerifyResetCodeBodyDto))]
[JsonSerializable(typeof(ResetPasswordBodyDto))]
[JsonSerializable(typeof(SwitchChurchBodyDto))]
[JsonSerializable(typeof(ChangePasswordBodyDto))]
[JsonSerializable(typeof(FullNameBodyDto))]
[JsonSerializable(typeof(MessageDto))]
[JsonSerializable(typeof(ValidDto))]
[JsonSerializable(typeof(HealthDto))]
[JsonSerializable(typeof(ApiErrorDto))]
[JsonSerializable(typeof(List<DeviceSessionDto>))]
[JsonSerializable(typeof(ChurchDto))]
[JsonSerializable(typeof(ChurchModulesDto))]
[JsonSerializable(typeof(ProjectionSettingsDto))]
[JsonSerializable(typeof(StorageBreakdownDto))]
[JsonSerializable(typeof(ChurchPatchDto))]
[JsonSerializable(typeof(PersonDto))]
[JsonSerializable(typeof(List<PersonDto>))]
[JsonSerializable(typeof(PersonCreateDto))]
[JsonSerializable(typeof(PersonRenameDto))]
[JsonSerializable(typeof(ServiceTypeDto))]
[JsonSerializable(typeof(List<ServiceTypeDto>))]
[JsonSerializable(typeof(ServiceTypeInputDto))]
[JsonSerializable(typeof(ServiceTypeCreateDto))]
[JsonSerializable(typeof(SongDto))]
[JsonSerializable(typeof(SongInputDto))]
[JsonSerializable(typeof(SongCreateDto))]
[JsonSerializable(typeof(List<SongSummaryDto>))]
[JsonSerializable(typeof(MediaAssetDto))]
[JsonSerializable(typeof(MediaUploadRequestDto))]
[JsonSerializable(typeof(UploadTicketDto))]
[JsonSerializable(typeof(MediaConfirmDto))]
[JsonSerializable(typeof(MediaPatchDto))]
[JsonSerializable(typeof(List<MediaAssetDto>))]
[JsonSerializable(typeof(DownloadUrlDto))]
[JsonSerializable(typeof(ServiceRecordDto))]
[JsonSerializable(typeof(List<ServiceRecordDto>))]
[JsonSerializable(typeof(ServiceRecordInputDto))]
[JsonSerializable(typeof(BlockActualPatchDto))]
[JsonSerializable(typeof(BlockPersonPatchDto))]
[JsonSerializable(typeof(SyncPageDto))]
[JsonSerializable(typeof(List<BibleTranslationDto>))]
[JsonSerializable(typeof(List<BibleBookDto>))]
[JsonSerializable(typeof(BibleChapterDto))]
[JsonSerializable(typeof(BibleDownloadDto))]
[JsonSerializable(typeof(BibleEnvelopeDto))]
[JsonSerializable(typeof(BibleMetaDto))]
public sealed partial class IrisJsonContext : JsonSerializerContext
{
}
