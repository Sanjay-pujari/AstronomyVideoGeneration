namespace Astronomy.MediaFactory.Core.DocumentaryBlueprint;
public sealed class DocumentaryExportSpecificationSummarizer
{
    public DocumentaryExportSpecificationSummary Summarize(DocumentaryExportSpecification specification)
    { ArgumentNullException.ThrowIfNull(specification);DocumentaryExportSpecificationValidator.ValidateSpecification(specification);return new(specification.ExportSpecificationId,specification.Manifest.ManifestId,specification.CertificationId,specification.ProvenanceId,specification.PackageId,specification.ReleaseCandidateId,specification.ConvergenceId,specification.Profile,specification.Manifest.Encoding,specification.ItemCount,specification.RequiredItemCount,specification.Items.Sum(x=>x.Dependencies.Count),specification.Items.Select(x=>x.ItemType).ToArray(),specification.Items.Select(x=>x.ContentType).ToArray(),specification.Metadata.CreatedUtc,specification.Metadata.CreatedBy,true); }
}
