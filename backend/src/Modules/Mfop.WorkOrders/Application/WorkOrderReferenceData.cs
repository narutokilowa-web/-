using System.Text.Json;
using System.Text.Json.Nodes;
using Mfop.Infrastructure.Persistence;
using Mfop.Kernel;
using Mfop.Kernel.Auditing;
using Mfop.Kernel.Commands;
using Mfop.Kernel.Results;
using Mfop.WorkOrders.Contracts;
using Mfop.WorkOrders.Domain;
using Mfop.WorkOrders.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Mfop.WorkOrders.Application;

/// <summary>
/// Diagnosis codes and checklist templates: seeded reference data (deploy/seed/reference-data.json), loaded by the migrator
/// through an audited command (actor SYSTEM). Nothing here is municipality policy; it is the pilot's field vocabulary.
/// A published template version is never edited in place (append-only table); a changed template is a new version.
/// </summary>
public sealed class WorkOrderReferenceData(WorkOrdersDbContext db) : IWorkOrderReference
{
    public static readonly EvidenceRequirementsDto DefaultRequirements = new(2, 1, 2, 1);

    public async Task<FieldReferenceData> ReferenceAsync(CancellationToken ct)
    {
        var codes = await db.DiagnosisCodes.AsNoTracking().OrderBy(d => d.Code).ToListAsync(ct);
        var templates = await db.ChecklistTemplates.AsNoTracking().OrderBy(t => t.Code).ThenBy(t => t.TemplateVersion).ToListAsync(ct);
        return new FieldReferenceData(codes.Select(ToDto).ToList(), templates.Select(ToDto).ToList());
    }

    public async Task<ChecklistTemplateDto?> TemplateAsync(Guid id, CancellationToken ct) =>
        await db.ChecklistTemplates.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, ct) is { } t ? ToDto(t) : null;

    public async Task<ChecklistTemplate?> TemplateForAsync(string powerSource, CancellationToken ct) =>
        await db.ChecklistTemplates.AsNoTracking().Where(t => t.PowerSources.Contains(powerSource))
            .OrderByDescending(t => t.TemplateVersion).FirstOrDefaultAsync(ct);

    public async Task<EvidenceRequirementsDto> RequirementsForAsync(Guid? templateId, CancellationToken ct) =>
        templateId is { } id && await TemplateAsync(id, ct) is { } t ? t.EvidenceRequirements : DefaultRequirements;

    public static DiagnosisCodeDto ToDto(DiagnosisCode d) =>
        new(d.Code, d.NameAr, d.NameEn, d.CountsAsFailure, d.RequiresMaterial, d.ExpectedItems, d.PowerSources, d.Path);

    public static ChecklistTemplateDto ToDto(ChecklistTemplate t)
    {
        using var items = JsonDocument.Parse(t.Items);
        using var methods = JsonDocument.Parse(t.TestMethods);
        using var requirements = JsonDocument.Parse(t.EvidenceRequirements);
        var r = requirements.RootElement;
        return new ChecklistTemplateDto(
            t.Id, t.Code, t.TemplateVersion, t.PowerSources,
            items.RootElement.EnumerateArray().Select(i => new ChecklistItemDto(
                i.GetProperty("code").GetString()!, i.GetProperty("text_ar").GetString()!, i.GetProperty("text_en").GetString()!,
                i.GetProperty("expected").GetString()!, i.GetProperty("blocking").GetBoolean(), i.GetProperty("test_item").GetBoolean())).ToList(),
            methods.RootElement.EnumerateArray().Select(m => new TestMethodDto(
                m.GetProperty("code").GetString()!, m.GetProperty("text_ar").GetString()!, m.GetProperty("text_en").GetString()!)).ToList(),
            new EvidenceRequirementsDto(r.GetProperty("before_first_arrival").GetInt32(), r.GetProperty("before_later_arrival").GetInt32(),
                r.GetProperty("after").GetInt32(), r.GetProperty("test").GetInt32()));
    }
}

/// <summary>Loads deploy/seed/reference-data.json (idempotent; only new codes and new template versions are added).</summary>
public sealed class ReferenceDataLoader(WorkOrdersDbContext db, IUnitOfWork uow, IAuditTrail audit, TimeProvider clock)
{
    public Task<Result<int>> LoadAsync(string json, CancellationToken ct) =>
        uow.ExecuteAsync<int>(CommandEnvelope.For("LoadReferenceData", ActorContext.System, "BOOTSTRAP_REFERENCE_DATA"), async innerCt =>
        {
            var root = JsonNode.Parse(json)!.AsObject();
            var added = 0;
            foreach (var node in root["diagnosis_codes"]!.AsArray())
            {
                var code = node!["code"]!.GetValue<string>();
                if (await db.DiagnosisCodes.AnyAsync(d => d.Code == code, innerCt)) continue;
                var d = new DiagnosisCode(Ids.New(), code, node["name_ar"]!.GetValue<string>(), node["name_en"]!.GetValue<string>(),
                    node["counts_as_failure"]!.GetValue<bool>(), node["requires_material"]!.GetValue<bool>(),
                    node["expected_items"]!.AsArray().Select(x => x!.GetValue<string>()).ToArray(),
                    node["power_sources"]!.AsArray().Select(x => x!.GetValue<string>()).ToArray(), node["path"]!.GetValue<string>());
                db.DiagnosisCodes.Add(d);
                audit.Record(d, "diagnosis_code.defined");
                added++;
            }

            foreach (var node in root["checklist_templates"]!.AsArray())
            {
                var code = node!["code"]!.GetValue<string>();
                var version = node["version"]!.GetValue<int>();
                if (await db.ChecklistTemplates.AnyAsync(t => t.Code == code && t.TemplateVersion == version, innerCt)) continue;
                var t = new ChecklistTemplate(Ids.New(), code, version, node["power_sources"]!.AsArray().Select(x => x!.GetValue<string>()).ToArray(),
                    node["items"]!.ToJsonString(), node["test_methods"]!.ToJsonString(), node["evidence_requirements"]!.ToJsonString(), clock.UtcNowMicros());
                db.ChecklistTemplates.Add(t);
                audit.Record(t, "checklist_template.published");
                added++;
            }

            return added;
        }, ct);
}
