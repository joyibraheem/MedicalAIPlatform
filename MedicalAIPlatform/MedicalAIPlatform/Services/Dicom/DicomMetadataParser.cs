using FellowOakDicom;
using FellowOakDicom.Imaging;
using MedicalAIPlatform.Models.Dicom;
using MedicalAIPlatform.Options;

namespace MedicalAIPlatform.Services.Dicom;

public sealed class DicomMetadataParser
{
    private readonly ILogger<DicomMetadataParser> _log;
    private readonly DicomPipelineOptions _pipeline;

    public DicomMetadataParser(
        ILogger<DicomMetadataParser> log,
        Microsoft.Extensions.Options.IOptions<DicomPipelineOptions> pipeline)
    {
        _log = log;
        _pipeline = pipeline.Value;
    }

    public PatientMedicalHistory Parse(DicomFile file)
    {
        var ds = file.Dataset;
        var m = new PatientMedicalHistory();

        m.PatientId = ReadString(ds, DicomTag.PatientID);

        var rawName = ReadString(ds, DicomTag.PatientName);
        m.PatientName = PatientNameFriendly(rawName);

        m.PatientAgeYears = TryParsePatientAge(ds);
        m.PatientSex = ReadString(ds, DicomTag.PatientSex);
        m.StudyInstanceUid = ReadString(ds, DicomTag.StudyInstanceUID);
        m.StudyId = ReadString(ds, DicomTag.StudyID);
        m.Modality = ReadString(ds, DicomTag.Modality);
        m.BodyPartExamined = ReadString(ds, DicomTag.BodyPartExamined);
        m.SeriesDescription = ReadString(ds, DicomTag.SeriesDescription);
        m.StudyDescription = ReadString(ds, DicomTag.StudyDescription);

        m.StudyDateTime = CombineStudyDateTime(ds);

        try
        {
            var dicomImage = new DicomImage(ds);
            m.NumberOfFrames = Math.Max(1, dicomImage.NumberOfFrames);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Unable to read frame count; assuming single frame.");
            m.NumberOfFrames = 1;
        }

        if (TryReadWindow(ds, out double wc, out double ww))
        {
            m.WindowCenter = wc;
            m.WindowWidth = ww;
        }
        else if (string.Equals(m.Modality, "CT", StringComparison.OrdinalIgnoreCase))
        {
            m.WindowCenter = _pipeline.DefaultCtWindowCenter;
            m.WindowWidth = _pipeline.DefaultCtWindowWidth;
            m.AdditionalTags["window_source"] = "default_ct_fallback";
            _log.LogInformation("Applying default CT window C={C} W={W} (tags missing)",
                _pipeline.DefaultCtWindowCenter, _pipeline.DefaultCtWindowWidth);
        }

        m.AdditionalTags["transfer_syntax_uid"] = file.FileMetaInfo?.TransferSyntax?.UID?.UID ?? "";
        m.AdditionalTags["implementation_version"] = file.FileMetaInfo?.Version?.ToString() ?? "";

        return m;
    }

    private DateTimeOffset? CombineStudyDateTime(DicomDataset ds)
    {
        try
        {
            var date = ReadString(ds, DicomTag.StudyDate);
            var time = ReadString(ds, DicomTag.StudyTime);
            if (string.IsNullOrWhiteSpace(date) || date.Length < 8) return null;
            if (!int.TryParse(date.AsSpan(0, 4), out int year) ||
                !int.TryParse(date.AsSpan(4, 2), out int mo) ||
                !int.TryParse(date.AsSpan(6, 2), out int d))
                return null;

            int hh = 0, mi = 0, ss = 0, fracMs = 0;
            if (!string.IsNullOrWhiteSpace(time))
            {
                var t = time.Split('.', StringSplitOptions.RemoveEmptyEntries)[0];
                if (t.Length >= 6)
                {
                    int.TryParse(t.AsSpan(0, 2), out hh);
                    int.TryParse(t.AsSpan(2, 2), out mi);
                    int.TryParse(t.AsSpan(4, 2), out ss);
                }
                if (time.Contains('.'))
                {
                    var parts = time.Split('.');
                    if (parts.Length > 1 && parts[1].Length > 0)
                    {
                        var digits = parts[1].TrimEnd(',', ' ');
                        digits = digits.Length > 3 ? digits[..3] : digits;
                        int.TryParse(digits, out fracMs);
                    }
                }
            }

            try
            {
                return new DateTimeOffset(year, mo, d, hh, mi, ss, fracMs, TimeSpan.Zero);
            }
            catch
            {
                return null;
            }
        }
        catch
        {
            return null;
        }
    }

    private static int? TryParsePatientAge(DicomDataset ds)
    {
        // PatientAge is AS (Age String) VR e.g. "045Y"; CS sometimes used for patient age qualifiers
        var asStr = ReadString(ds, DicomTag.PatientAge);
        if (!string.IsNullOrWhiteSpace(asStr))
        {
            var digits = new string(asStr.TakeWhile(char.IsDigit).ToArray());
            if (int.TryParse(digits, out int yrs)) return yrs;
        }

        try
        {
            if (!ds.TryGetSingleValue(DicomTag.PatientBirthDate, out string dob)) return null;
            if (string.IsNullOrWhiteSpace(dob) || dob.Length < 8) return null;
            var y = int.Parse(dob.AsSpan(0, 4));
            var mo = int.Parse(dob.AsSpan(4, 2));
            var day = int.Parse(dob.AsSpan(6, 2));
            var birth = new DateOnly(y, mo, day);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var years = today.Year - birth.Year;
            if (birth > today.AddYears(-years)) years--;
            return years >= 0 && years <= 130 ? years : null;
        }
        catch { return null; }
    }

    private static bool TryReadWindow(DicomDataset ds, out double wc, out double ww)
    {
        wc = 0; ww = 0;
        var cw = ParseFirstDsValue(ds, DicomTag.WindowCenter);
        var w = ParseFirstDsValue(ds, DicomTag.WindowWidth);
        if (cw is { } c && w is { } wwv && wwv > 0)
        {
            wc = c;
            ww = wwv;
            return true;
        }

        return false;
    }

    private static double? ParseFirstDsValue(DicomDataset ds, DicomTag tag)
    {
        try
        {
            if (!ds.Contains(tag)) return null;
            var raw = ds.GetSingleValue<string>(tag)?.Trim();
            if (string.IsNullOrEmpty(raw)) return null;
            var part = raw.Split('\\')[0];
            return double.TryParse(part, System.Globalization.CultureInfo.InvariantCulture, out var d)
                ? d : null;
        }
        catch
        {
            return null;
        }
    }

    private static string ReadString(DicomDataset ds, DicomTag tag)
    {
        try
        {
            if (!ds.Contains(tag)) return "";
            return ds.GetSingleValue<string>(tag) ?? "";
        }
        catch (DicomDataException)
        {
            return "";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return "";
        }
    }

    private static string PatientNameFriendly(string pn)
        => string.Join(" ",
            pn.Split('^', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
