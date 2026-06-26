using System.Text.Json;
using MedicalAIPlatform.Data;
using MedicalAIPlatform.Hubs;
using MedicalAIPlatform.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedicalAIPlatform.Services;

internal sealed record CtJobInputPayload(string FileName, string ContentType, string TempPath);

/// <summary>Database-backed analytics jobs with server-side execution (survives navigation). Notifies users via SignalR.</summary>
public sealed class AnalyticsJobQueueService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IUserAnalyticsSessionStore _sessionStore;
    private readonly IHubContext<AssistantHub> _hubContext;
    private readonly ILogger<AnalyticsJobQueueService> _logger;

    private static readonly TimeSpan JobRetention = TimeSpan.FromHours(48);

    public AnalyticsJobQueueService(
        IServiceScopeFactory scopeFactory,
        IUserAnalyticsSessionStore sessionStore,
        IHubContext<AssistantHub> hubContext,
        ILogger<AnalyticsJobQueueService> logger)
    {
        _scopeFactory = scopeFactory;
        _sessionStore = sessionStore;
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <summary>Queues CT inference; persists job and input to disk before returning. Processing continues if the user navigates away.</summary>
    public async Task<Guid> StartCtJobAsync(string userId, byte[] fileBytes, string safeFileName, string contentType,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User id is required.", nameof(userId));

        PruneExpiredJobsFireAndForget();

        var id = Guid.NewGuid();
        var dir = Path.Combine(Path.GetTempPath(), "MedicalAiPlatform", "jobs");
        Directory.CreateDirectory(dir);
        var tempPath = Path.Combine(dir, id.ToString("N"));
        await File.WriteAllBytesAsync(tempPath, fileBytes, cancellationToken).ConfigureAwait(false);

        var inputJson = JsonSerializer.Serialize(
            new CtJobInputPayload(safeFileName, contentType ?? "application/octet-stream", tempPath),
            JsonOpts);

        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.ChestAiBackgroundJobs.Add(new ChestAiBackgroundJob
            {
                Id = id,
                UserId = userId,
                Kind = ChestAiBackgroundJob.KindCt,
                Status = ChestAiBackgroundJob.StatusQueued,
                InputPayloadJson = inputJson,
                CreatedAt = DateTimeOffset.UtcNow
            });

            db.ChestAiChatMessages.Add(new ChestAiChatMessageEntity
            {
                UserId = userId,
                Role = "system",
                Content = "📥 Scan received — processing started. File: " + safeFileName,
                MetadataJson = JsonSerializer.Serialize(new { kind = "ct-queued" }, JsonOpts),
                RelatedJobId = id,
                CreatedAt = DateTimeOffset.UtcNow
            });

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        _ = Task.Run(() => RunCtJobAsync(id, tempPath));

        _logger.LogInformation("Analytics CT job {JobId} queued (user={User}, file={File}, bytes={Len})", id, userId,
            safeFileName, fileBytes.Length);
        return id;
    }

    /// <summary>Queues CheXNet chest inference (same persistence / SignalR / chat UX as CT jobs).</summary>
    public async Task<Guid> StartXRayJobAsync(string userId, byte[] fileBytes, string safeFileName, string contentType,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User id is required.", nameof(userId));

        PruneExpiredJobsFireAndForget();

        var id = Guid.NewGuid();
        var dir = Path.Combine(Path.GetTempPath(), "MedicalAiPlatform", "jobs");
        Directory.CreateDirectory(dir);
        var tempPath = Path.Combine(dir, id.ToString("N"));
        await File.WriteAllBytesAsync(tempPath, fileBytes, cancellationToken).ConfigureAwait(false);

        var inputJson = JsonSerializer.Serialize(
            new CtJobInputPayload(safeFileName, contentType ?? "application/octet-stream", tempPath),
            JsonOpts);

        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.ChestAiBackgroundJobs.Add(new ChestAiBackgroundJob
            {
                Id = id,
                UserId = userId,
                Kind = ChestAiBackgroundJob.KindXRay,
                Status = ChestAiBackgroundJob.StatusQueued,
                InputPayloadJson = inputJson,
                CreatedAt = DateTimeOffset.UtcNow
            });

            db.ChestAiChatMessages.Add(new ChestAiChatMessageEntity
            {
                UserId = userId,
                Role = "system",
                Content = "📥 Scan received — processing started. File: " + safeFileName,
                MetadataJson = JsonSerializer.Serialize(new { kind = "xray-queued" }, JsonOpts),
                RelatedJobId = id,
                CreatedAt = DateTimeOffset.UtcNow
            });

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        _ = Task.Run(() => RunXRayJobAsync(id, tempPath));

        _logger.LogInformation("Analytics X-ray job {JobId} queued (user={User}, file={File}, bytes={Len})", id, userId,
            safeFileName, fileBytes.Length);
        return id;
    }

    /// <summary>Persists user message and queues assistant reply generation.</summary>
    public async Task<Guid> EnqueueAssistantChatAsync(
        string userId,
        string message,
        IList<ChatMessage> priorHistory,
        int? patientContextId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User id is required.", nameof(userId));
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("Message is required.", nameof(message));

        var jobId = Guid.NewGuid();

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        db.ChestAiChatMessages.Add(new ChestAiChatMessageEntity
        {
            UserId = userId,
            Role = "user",
            Content = message.Trim(),
            RelatedJobId = jobId,
            CreatedAt = DateTimeOffset.UtcNow
        });

        var payload = new AssistantChatJobPayload
        {
            Message = message.Trim(),
            History = priorHistory?.ToList() ?? [],
            PatientContextId = patientContextId
        };

        db.ChestAiBackgroundJobs.Add(new ChestAiBackgroundJob
        {
            Id = jobId,
            UserId = userId,
            Kind = ChestAiBackgroundJob.KindAssistantChat,
            Status = ChestAiBackgroundJob.StatusQueued,
            InputPayloadJson = JsonSerializer.Serialize(payload, JsonOpts),
            CreatedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _ = Task.Run(() => RunAssistantJobAsync(jobId));
        _logger.LogInformation("Assistant chat job {JobId} queued for user {User}", jobId, userId);
        return jobId;
    }

    public async Task<AnalyticsJobStatusDto?> GetStatusForUserAsync(Guid jobId, string userId,
        CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var job = await db.ChestAiBackgroundJobs.AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == jobId && j.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (job is null)
            return null;

        return MapJobToDto(job);
    }

    private static AnalyticsJobStatusDto MapJobToDto(ChestAiBackgroundJob job)
    {
        AnalyticsCtJobResultDto? result = null;
        string? assistantContent = null;

            if (!string.IsNullOrWhiteSpace(job.ResultPayloadJson))
            {
                if (job.Kind is ChestAiBackgroundJob.KindCt or ChestAiBackgroundJob.KindXRay)
                {
                    AnalyticsJobResultEnvelope.TryParse(job.ResultPayloadJson, out result, out _);
                }
                else if (job.Kind == ChestAiBackgroundJob.KindAssistantChat)
            {
                try
                {
                    using var doc = JsonDocument.Parse(job.ResultPayloadJson);
                    if (doc.RootElement.TryGetProperty("assistantContent", out var ac))
                        assistantContent = ac.GetString();
                }
                catch
                {
                    /* ignore */
                }
            }
        }

        return new AnalyticsJobStatusDto
        {
            Kind = job.Kind,
            Status = job.Status,
            Result = result,
            Error = job.ErrorMessage,
            AssistantContent = assistantContent
        };
    }

    private async Task RunCtJobAsync(Guid jobId, string tempPath)
    {
        string userId = "";

        try
        {
            await using var ctDbScopeStart = _scopeFactory.CreateAsyncScope();
            var dbStart = ctDbScopeStart.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = await dbStart.ChestAiBackgroundJobs.FirstOrDefaultAsync(j => j.Id == jobId)
                .ConfigureAwait(false);
            if (row is null)
            {
                TryDeleteFile(tempPath);
                return;
            }

            userId = row.UserId;
            row.Status = ChestAiBackgroundJob.StatusProcessing;
            await dbStart.SaveChangesAsync().ConfigureAwait(false);

            CtJobInputPayload? input;
            try
            {
                input = JsonSerializer.Deserialize<CtJobInputPayload>(row.InputPayloadJson ?? "{}", JsonOpts);
            }
            catch (Exception ex)
            {
                await FailJobAsync(jobId, userId, ChestAiBackgroundJob.KindCt, "Invalid job payload: " + ex.Message)
                    .ConfigureAwait(false);
                return;
            }

            if (input is null || string.IsNullOrWhiteSpace(input.TempPath))
            {
                await FailJobAsync(jobId, userId, ChestAiBackgroundJob.KindCt, "Missing temp file path.")
                    .ConfigureAwait(false);
                return;
            }

            byte[] bytes;
            try
            {
                bytes = await File.ReadAllBytesAsync(input.TempPath).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await FailJobAsync(jobId, userId, ChestAiBackgroundJob.KindCt, "Could not read uploaded file: " + ex.Message)
                    .ConfigureAwait(false);
                return;
            }

            await using var scope = _scopeFactory.CreateAsyncScope();
            var executor = scope.ServiceProvider.GetRequiredService<AnalyticsCtInferenceExecutor>();

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(30));
            var lung = await executor.PredictCtAsync(bytes, input.FileName, input.ContentType, cts.Token)
                .ConfigureAwait(false);

            if (!string.IsNullOrEmpty(lung.Error))
            {
                await FailJobAsync(jobId, userId, ChestAiBackgroundJob.KindCt, lung.Error).ConfigureAwait(false);
                return;
            }

            string? ctPreviewUrl = null;
            if (!AnalyticsDicomRouting.IsDicomUpload(input.FileName, input.ContentType))
                ctPreviewUrl = MakeDataUrl(bytes, input.ContentType);
            else if (!string.IsNullOrEmpty(lung.PreviewImageDataUrl))
                ctPreviewUrl = lung.PreviewImageDataUrl;

            var viewState = AnalyticsSessionSnapshot.FromSetResults(
                null, null, lung, null, ctPreviewUrl, null, null, pipelineNotesProvided: false);
            try
            {
                var assets = scope.ServiceProvider.GetRequiredService<TrainingAssetPersistenceService>();
                var source = await assets.CopyJobAssetAsync(
                    ModelTrainingNames.LungCancer, userId, jobId, input.TempPath, input.FileName, input.ContentType)
                    .ConfigureAwait(false);
                viewState = AnalyticsSessionSnapshot.FromSetResults(
                    null, null, lung, null, ctPreviewUrl, null, null, pipelineNotesProvided: false,
                    lungCancerSourceJson: source.ToJson(), relatedJobId: jobId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not persist CT training asset for job {JobId}", jobId);
            }

            _sessionStore.SetForJob(userId, jobId, viewState);

            var topProbs = lung.Probabilities
                .OrderByDescending(kv => kv.Value)
                .Take(8)
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

            var summary =
                $"Chest CT analysis finished. Top prediction: **{lung.PredictedClass}**."
                + (topProbs.Count > 0
                    ? " Key scores: " + string.Join(", ", topProbs.Select(kv => $"{kv.Key} {kv.Value:P0}"))
                    : "");

            var dto = new AnalyticsCtJobResultDto
            {
                Redirect = $"/Analytics/CT?jobId={jobId:D}",
                PredictedClass = lung.PredictedClass,
                ScanType = lung.ScanType,
                Summary = summary,
                TopProbabilities = topProbs,
                InferenceNote = lung.ScanType.Contains("DICOM", StringComparison.OrdinalIgnoreCase)
                    ? "Aggregated over DICOM slices."
                    : null,
            };

            var resultJson = new AnalyticsJobResultEnvelope
            {
                Summary = dto,
                ViewState = viewState
            }.ToJson();

            await using var ctDbScopeDone = _scopeFactory.CreateAsyncScope();
            var dbDone = ctDbScopeDone.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var doneRow = await dbDone.ChestAiBackgroundJobs.FirstAsync(j => j.Id == jobId).ConfigureAwait(false);
            doneRow.Status = ChestAiBackgroundJob.StatusDone;
            doneRow.ResultPayloadJson = resultJson;
            doneRow.CompletedAt = DateTimeOffset.UtcNow;
            doneRow.ErrorMessage = null;
            await dbDone.SaveChangesAsync().ConfigureAwait(false);

            await AppendChatLineAsync(userId, "assistant", dto.Summary, jobId,
                    new { kind = "ct-result", redirect = dto.Redirect })
                .ConfigureAwait(false);

            await PushJobUpdateAsync(userId, new ChestAiJobPushDto
            {
                Kind = ChestAiBackgroundJob.KindCt,
                JobId = jobId,
                Status = ChestAiBackgroundJob.StatusDone,
                Result = dto
            }).ConfigureAwait(false);

            _logger.LogInformation("Analytics CT job {JobId} completed successfully", jobId);
        }
        catch (OperationCanceledException)
        {
            await FailJobAsync(jobId, userId, ChestAiBackgroundJob.KindCt, "Analysis timed out or was cancelled.")
                .ConfigureAwait(false);
            _logger.LogWarning("Analytics CT job {JobId} cancelled or timed out", jobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Analytics CT job {JobId} threw unexpectedly", jobId);
            await FailJobAsync(jobId, userId, ChestAiBackgroundJob.KindCt, ex.Message).ConfigureAwait(false);
        }
        finally
        {
            TryDeleteFile(tempPath);
        }
    }

    private async Task RunXRayJobAsync(Guid jobId, string tempPath)
    {
        string userId = "";

        try
        {
            await using var dbScopeStart = _scopeFactory.CreateAsyncScope();
            var dbStart = dbScopeStart.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = await dbStart.ChestAiBackgroundJobs.FirstOrDefaultAsync(j => j.Id == jobId)
                .ConfigureAwait(false);
            if (row is null)
            {
                TryDeleteFile(tempPath);
                return;
            }

            if (row.Kind != ChestAiBackgroundJob.KindXRay)
            {
                TryDeleteFile(tempPath);
                return;
            }

            userId = row.UserId;
            row.Status = ChestAiBackgroundJob.StatusProcessing;
            await dbStart.SaveChangesAsync().ConfigureAwait(false);

            CtJobInputPayload? input;
            try
            {
                input = JsonSerializer.Deserialize<CtJobInputPayload>(row.InputPayloadJson ?? "{}", JsonOpts);
            }
            catch (Exception ex)
            {
                await FailJobAsync(jobId, userId, ChestAiBackgroundJob.KindXRay, "Invalid job payload: " + ex.Message)
                    .ConfigureAwait(false);
                return;
            }

            if (input is null || string.IsNullOrWhiteSpace(input.TempPath))
            {
                await FailJobAsync(jobId, userId, ChestAiBackgroundJob.KindXRay, "Missing temp file path.")
                    .ConfigureAwait(false);
                return;
            }

            byte[] bytes;
            try
            {
                bytes = await File.ReadAllBytesAsync(input.TempPath).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await FailJobAsync(jobId, userId, ChestAiBackgroundJob.KindXRay,
                        "Could not read uploaded file: " + ex.Message)
                    .ConfigureAwait(false);
                return;
            }

            await using var scope = _scopeFactory.CreateAsyncScope();
            var executor = scope.ServiceProvider.GetRequiredService<AnalyticsXRayInferenceExecutor>();

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(30));
            var (map, notes) = await executor.PredictChestAsync(bytes, input.FileName, input.ContentType, cts.Token)
                .ConfigureAwait(false);

            if (!map.TryGetValue("CheXNet", out var chex))
            {
                await FailJobAsync(jobId, userId, ChestAiBackgroundJob.KindXRay,
                        "CheXNet response missing from inference service.")
                    .ConfigureAwait(false);
                return;
            }

            string? previewUrl = null;
            if (!AnalyticsDicomRouting.IsDicomUpload(input.FileName, input.ContentType))
                previewUrl = MakeDataUrl(bytes, input.ContentType);
            else if (!string.IsNullOrEmpty(chex.PreviewImageDataUrl))
                previewUrl = chex.PreviewImageDataUrl;

            var viewState = AnalyticsSessionSnapshot.FromSetResults(
                map, null, null, previewUrl, null, null, notes, pipelineNotesProvided: notes is not null);
            try
            {
                var assets = scope.ServiceProvider.GetRequiredService<TrainingAssetPersistenceService>();
                var source = await assets.CopyJobAssetAsync(
                    ModelTrainingNames.CheXNet, userId, jobId, input.TempPath, input.FileName, input.ContentType)
                    .ConfigureAwait(false);
                viewState = AnalyticsSessionSnapshot.FromSetResults(
                    map, null, null, previewUrl, null, null, notes, pipelineNotesProvided: notes is not null,
                    cheXNetSourceJson: source.ToJson(), relatedJobId: jobId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not persist X-Ray training asset for job {JobId}", jobId);
            }

            _sessionStore.SetForJob(userId, jobId, viewState);

            string predicted = "Unknown";
            if (chex.TopK.FirstOrDefault() is { ClassName: { Length: > 0 } cn })
                predicted = cn;
            else if (chex.Probabilities.Count > 0)
                predicted = chex.Probabilities.OrderByDescending(kv => kv.Value).First().Key;

            var topProbs = chex.Probabilities
                .OrderByDescending(kv => kv.Value)
                .Take(8)
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

            var summary =
                $"Chest X-ray analysis finished. Top prediction: **{predicted}**."
                + (topProbs.Count > 0
                    ? " Key scores: " + string.Join(", ", topProbs.Select(kv => $"{kv.Key} {kv.Value:P0}"))
                    : "");

            var dto = new AnalyticsCtJobResultDto
            {
                Redirect = $"/Analytics/XRay?jobId={jobId:D}",
                PredictedClass = predicted,
                ScanType = "Chest X-Ray",
                Summary = summary,
                TopProbabilities = topProbs,
                InferenceNote = AnalyticsDicomRouting.IsDicomUpload(input.FileName, input.ContentType)
                    ? "CheXNet over DICOM slices (aggregated)."
                    : null
            };

            var resultJson = new AnalyticsJobResultEnvelope
            {
                Summary = dto,
                ViewState = viewState
            }.ToJson();

            await using var dbScopeDone = _scopeFactory.CreateAsyncScope();
            var dbDone = dbScopeDone.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var doneRow = await dbDone.ChestAiBackgroundJobs.FirstAsync(j => j.Id == jobId).ConfigureAwait(false);
            doneRow.Status = ChestAiBackgroundJob.StatusDone;
            doneRow.ResultPayloadJson = resultJson;
            doneRow.CompletedAt = DateTimeOffset.UtcNow;
            doneRow.ErrorMessage = null;
            await dbDone.SaveChangesAsync().ConfigureAwait(false);

            await AppendChatLineAsync(userId, "assistant", dto.Summary, jobId,
                    new { kind = "xray-result", redirect = dto.Redirect })
                .ConfigureAwait(false);

            await PushJobUpdateAsync(userId, new ChestAiJobPushDto
            {
                Kind = ChestAiBackgroundJob.KindXRay,
                JobId = jobId,
                Status = ChestAiBackgroundJob.StatusDone,
                Result = dto
            }).ConfigureAwait(false);

            _logger.LogInformation("Analytics X-ray job {JobId} completed successfully", jobId);
        }
        catch (OperationCanceledException)
        {
            await FailJobAsync(jobId, userId, ChestAiBackgroundJob.KindXRay, "Analysis timed out or was cancelled.")
                .ConfigureAwait(false);
            _logger.LogWarning("Analytics X-ray job {JobId} cancelled or timed out", jobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Analytics X-ray job {JobId} threw unexpectedly", jobId);
            await FailJobAsync(jobId, userId, ChestAiBackgroundJob.KindXRay, ex.Message).ConfigureAwait(false);
        }
        finally
        {
            TryDeleteFile(tempPath);
        }
    }

    private async Task RunAssistantJobAsync(Guid jobId)
    {
        string userId = "";

        try
        {
            await using var asstDbScopeStart = _scopeFactory.CreateAsyncScope();
            var dbStart = asstDbScopeStart.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = await dbStart.ChestAiBackgroundJobs.FirstOrDefaultAsync(j => j.Id == jobId)
                .ConfigureAwait(false);
            if (row is null || row.Kind != ChestAiBackgroundJob.KindAssistantChat)
                return;

            userId = row.UserId;
            row.Status = ChestAiBackgroundJob.StatusProcessing;
            await dbStart.SaveChangesAsync().ConfigureAwait(false);

            AssistantChatJobPayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<AssistantChatJobPayload>(row.InputPayloadJson ?? "{}", JsonOpts);
            }
            catch (Exception ex)
            {
                await FailJobAsync(jobId, userId, ChestAiBackgroundJob.KindAssistantChat,
                    "Invalid chat payload: " + ex.Message).ConfigureAwait(false);
                return;
            }

            if (payload is null || string.IsNullOrWhiteSpace(payload.Message))
            {
                await FailJobAsync(jobId, userId, ChestAiBackgroundJob.KindAssistantChat, "Empty message.")
                    .ConfigureAwait(false);
                return;
            }

            string reply;
            object? metadata = null;

            await using var scope = _scopeFactory.CreateAsyncScope();
            var chatService = scope.ServiceProvider.GetRequiredService<ChatService>();
            var reportSvc = scope.ServiceProvider.GetRequiredService<MedicalReportService>();
            var sessionSnapshot = _sessionStore.GetLatest(userId) ?? AnalyticsSessionSnapshot.Empty;

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(6));

            if (MedicalReportIntentParser.IsGenerateMedicalReportIntent(payload.Message))
            {
                if (!MedicalReportIntentParser.TryResolvePatientId(payload.Message, payload.PatientContextId,
                        out var pid))
                {
                    reply =
                        "To generate a structured medical report, open a patient chart first (the assistant picks up patient context from that page), or include an explicit chart ID in your message — for example: \"generate medical report for patient 12\".";
                }
                else
                {
                    try
                    {
                        var reportId = await reportSvc.GenerateAndPersistAsync(pid, userId, cancellationToken: cts.Token)
                            .ConfigureAwait(false);
                        var path = $"/MedicalReport/Details/{reportId:D}";
                        reply =
                            $"Structured medical report generated.\n\nOpen the report card to review narrative sections, apply physician edits, print, or download PDF:\n{path}";
                        metadata = new { kind = "medical-report", reportId, patientId = pid, url = path };
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Medical report generation failed for patient {PatientId}", pid);
                        reply = $"Report generation failed: {ex.Message}";
                    }
                }
            }
            else if (MedicalReportIntentParser.IsExplainMedicalReportIntent(payload.Message))
            {
                if (!MedicalReportIntentParser.TryResolvePatientId(payload.Message, payload.PatientContextId,
                        out var expPid))
                {
                    reply =
                        "To explain or summarize the structured medical report, open a patient chart first or include the chart ID in your message (for example: \"explain the report for patient 12\").";
                }
                else
                {
                    var ctxJson = await reportSvc.GetLatestReportContextJsonAsync(expPid, cts.Token)
                        .ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(ctxJson))
                    {
                        reply =
                            "No saved structured medical report exists for this patient yet. Generate one from the patient chart or ask me to \"generate medical report\" while viewing that patient.";
                    }
                    else
                    {
                        var history = payload.History.ToList();
                        history.Add(new ChatMessage { Role = "user", Content = payload.Message });
                        reply = await chatService
                            .SendAsync(history, sessionSnapshot.CurrentResults, sessionSnapshot.CurrentBioBertResults,
                                sessionSnapshot.CurrentLungAIResults, ctxJson, cts.Token)
                            .ConfigureAwait(false);
                    }
                }
            }
            else
            {
                var history = payload.History.ToList();
                history.Add(new ChatMessage { Role = "user", Content = payload.Message });
                reply = await chatService
                    .SendAsync(history, sessionSnapshot.CurrentResults, sessionSnapshot.CurrentBioBertResults,
                        sessionSnapshot.CurrentLungAIResults, null, cts.Token)
                    .ConfigureAwait(false);
            }

            var resultJson = JsonSerializer.Serialize(new { assistantContent = reply }, JsonOpts);

            await using var asstDbScopeDone = _scopeFactory.CreateAsyncScope();
            var dbDone = asstDbScopeDone.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var doneRow = await dbDone.ChestAiBackgroundJobs.FirstAsync(j => j.Id == jobId).ConfigureAwait(false);
            doneRow.Status = ChestAiBackgroundJob.StatusDone;
            doneRow.ResultPayloadJson = resultJson;
            doneRow.CompletedAt = DateTimeOffset.UtcNow;
            doneRow.ErrorMessage = null;

            dbDone.ChestAiChatMessages.Add(new ChestAiChatMessageEntity
            {
                UserId = userId,
                Role = "assistant",
                Content = reply,
                RelatedJobId = jobId,
                MetadataJson = metadata is null ? null : JsonSerializer.Serialize(metadata, JsonOpts),
                CreatedAt = DateTimeOffset.UtcNow
            });

            await dbDone.SaveChangesAsync().ConfigureAwait(false);

            await PushJobUpdateAsync(userId, new ChestAiJobPushDto
            {
                Kind = ChestAiBackgroundJob.KindAssistantChat,
                JobId = jobId,
                Status = ChestAiBackgroundJob.StatusDone,
                AssistantContent = reply
            }).ConfigureAwait(false);

            _logger.LogInformation("Assistant chat job {JobId} completed", jobId);
        }
        catch (OperationCanceledException)
        {
            await FailJobAsync(jobId, userId, ChestAiBackgroundJob.KindAssistantChat, "Assistant reply timed out.")
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Assistant chat job {JobId} failed", jobId);
            await FailJobAsync(jobId, userId, ChestAiBackgroundJob.KindAssistantChat, ex.Message)
                .ConfigureAwait(false);
        }
    }

    private async Task FailJobAsync(Guid jobId, string userId, string kind, string message)
    {
        try
        {
            await using var failScope = _scopeFactory.CreateAsyncScope();
            var db = failScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = await db.ChestAiBackgroundJobs.FirstOrDefaultAsync(j => j.Id == jobId).ConfigureAwait(false);
            if (row is not null)
            {
                row.Status = ChestAiBackgroundJob.StatusFailed;
                row.ErrorMessage = message;
                row.CompletedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not persist failure for job {JobId}", jobId);
        }

        if (!string.IsNullOrEmpty(userId))
        {
            await PushJobUpdateAsync(userId, new ChestAiJobPushDto
            {
                Kind = kind,
                JobId = jobId,
                Status = ChestAiBackgroundJob.StatusFailed,
                Error = message
            }).ConfigureAwait(false);

            try
            {
                var (label, metaKind) = kind switch
                {
                    ChestAiBackgroundJob.KindCt => ("CT analysis", "ct-error"),
                    ChestAiBackgroundJob.KindXRay => ("Chest X-ray analysis", "xray-error"),
                    _ => ("Assistant", "assistant-error")
                };
                await AppendChatLineAsync(userId, "assistant", $"{label} failed: {message}", jobId,
                        new { kind = metaKind })
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not persist chat line for failed job {JobId}", jobId);
            }
        }

        _logger.LogWarning("Job {JobId} failed ({Kind}): {Message}", jobId, kind, message);
    }

    private async Task AppendChatLineAsync(string userId, string role, string content, Guid? relatedJobId,
        object? metadata)
    {
        await using var appendScope = _scopeFactory.CreateAsyncScope();
        var db = appendScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.ChestAiChatMessages.Add(new ChestAiChatMessageEntity
        {
            UserId = userId,
            Role = role,
            Content = content,
            RelatedJobId = relatedJobId,
            MetadataJson = metadata is null ? null : JsonSerializer.Serialize(metadata, JsonOpts),
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync().ConfigureAwait(false);
    }

    private Task PushJobUpdateAsync(string userId, ChestAiJobPushDto dto) =>
        _hubContext.Clients.Group(AssistantHub.GroupNameFor(userId)).SendAsync("jobUpdate", dto);

    private void PruneExpiredJobsFireAndForget()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var cutoff = DateTimeOffset.UtcNow - JobRetention;
                await using var pruneScope = _scopeFactory.CreateAsyncScope();
                var db = pruneScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var stale = await db.ChestAiBackgroundJobs
                    .Where(j => j.CompletedAt != null && j.CompletedAt < cutoff)
                    .ToListAsync()
                    .ConfigureAwait(false);

                if (stale.Count == 0)
                    return;

                db.ChestAiBackgroundJobs.RemoveRange(stale);
                await db.SaveChangesAsync().ConfigureAwait(false);
                _logger.LogInformation("Pruned {Count} stale background jobs", stale.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Job prune failed");
            }
        });
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            /* ignore */
        }
    }

    private static string MakeDataUrl(ReadOnlySpan<byte> bytes, string? contentType)
    {
        var mime = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType.Trim();
        return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
    }
}
