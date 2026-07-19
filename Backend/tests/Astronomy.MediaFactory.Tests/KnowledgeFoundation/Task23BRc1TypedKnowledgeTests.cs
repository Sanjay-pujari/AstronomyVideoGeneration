using System.Collections.ObjectModel;
using System.Reflection;
using Astronomy.MediaFactory.Core.AstronomyDomain.Taxonomy;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Classification;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Measurements;
using Astronomy.MediaFactory.Core.KnowledgeFoundation.TypedDomains.Physical;

namespace Astronomy.MediaFactory.Tests.KnowledgeFoundation;

public sealed class Task23BRc1TypedKnowledgeTests
{
    private static readonly AstronomyKnowledgeTypeId ClassificationTypeId = new("typed.classification.entity.v1");
    private static readonly AstronomyKnowledgeTypeId PhysicalTypeId = new("typed.physical.properties.v1");
    private static readonly AstronomyMeasurementUnit Kilometer = new("km", "km", AstronomyMeasurementDimension.Distance);
    private static readonly AstronomyMeasurementUnit Meter = new("m", "m", AstronomyMeasurementDimension.Distance);
    private static readonly AstronomyMeasurementUnit Kilogram = new("kg", "kg", AstronomyMeasurementDimension.Mass);

    [Fact]
    public void Classification_scheme_id_contract_is_complete()
    {
        var scheme = new AstronomyClassificationSchemeId("  Mixed.Case-Scheme  ");

        Assert.Equal("mixed.case-scheme", scheme.Value);
        Assert.Equal(new AstronomyClassificationSchemeId("mixed.case-scheme"), scheme);
        Assert.Equal("mixed.case-scheme", scheme.ToString());
        Assert.False(default(AstronomyClassificationSchemeId).IsValid);
        Assert.Throws<ArgumentException>(() => new AstronomyClassificationSchemeId(" "));
        Assert.Throws<ArgumentException>(() => new AstronomyClassificationSchemeId("bad token"));
        Assert.Throws<ArgumentException>(() => new AstronomyClassificationSchemeId("bad\n"));
        Assert.Equal(new string('a', 128), new AstronomyClassificationSchemeId(new string('A', 128)).Value);
        Assert.Throws<ArgumentException>(() => new AstronomyClassificationSchemeId(new string('a', 129)));
    }

    [Fact]
    public void Classification_value_contract_is_complete()
    {
        var value = new AstronomyClassificationValue("  SPECTRAL-G2V  ", "  Sun Like  ", "  Stable description  ");

        Assert.Equal("spectral-g2v", value.Code);
        Assert.Equal("Sun Like", value.DisplayName);
        Assert.Equal("Stable description", value.Description);
        Assert.Null(new AstronomyClassificationValue("code", "Name", " ").Description);
        Assert.Throws<ArgumentException>(() => new AstronomyClassificationValue(" ", "Name"));
        Assert.Throws<ArgumentException>(() => new AstronomyClassificationValue("code", " "));
        Assert.Throws<ArgumentException>(() => new AstronomyClassificationValue("bad code", "Name"));
        Assert.Throws<ArgumentException>(() => new AstronomyClassificationValue("code", "Bad\nName"));
        Assert.Throws<ArgumentException>(() => new AstronomyClassificationValue("code", "Name", "Bad\nDescription"));
        Assert.Equal(128, new AstronomyClassificationValue(new string('A', 128), "Name").Code.Length);
        Assert.Equal(160, new AstronomyClassificationValue("code", new string('N', 160)).DisplayName.Length);
        Assert.Equal(512, new AstronomyClassificationValue("code", "Name", new string('D', 512)).Description!.Length);
        Assert.Throws<ArgumentException>(() => new AstronomyClassificationValue(new string('a', 129), "Name"));
        Assert.Throws<ArgumentException>(() => new AstronomyClassificationValue("code", new string('N', 161)));
        Assert.Throws<ArgumentException>(() => new AstronomyClassificationValue("code", "Name", new string('D', 513)));
    }

    [Fact]
    public void Classification_qualifier_taxonomy_is_frozen_and_guarded()
    {
        Assert.Equal(new[] { "Primary", "Secondary", "Composite", "Provisional", "Historical", "Alternative" }, Enum.GetNames<AstronomyClassificationQualifier>());
        Assert.Equal(Enum.GetValues<AstronomyClassificationQualifier>().Length, Enum.GetValues<AstronomyClassificationQualifier>().Cast<int>().Distinct().Count());

        var scheme = new AstronomyClassificationSchemeId("scheme");
        var value = new AstronomyClassificationValue("code", "Name");
        foreach (var qualifier in Enum.GetValues<AstronomyClassificationQualifier>())
        {
            Assert.Equal(qualifier, new AstronomyClassificationAssignment(scheme, value, qualifier).Qualifier);
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => new AstronomyClassificationAssignment(scheme, value, (AstronomyClassificationQualifier)999));
    }

    [Fact]
    public void Classification_assignment_contract_is_complete()
    {
        var scheme = new AstronomyClassificationSchemeId("scheme");
        var value = new AstronomyClassificationValue("code", "Name");
        var assignment = new AstronomyClassificationAssignment(scheme, value, AstronomyClassificationQualifier.Secondary, " note ");
        var equivalent = new AstronomyClassificationAssignment(scheme, value, AstronomyClassificationQualifier.Secondary, "note");

        Assert.Throws<ArgumentNullException>(() => new AstronomyClassificationAssignment(scheme, null!, AstronomyClassificationQualifier.Primary));
        Assert.Throws<ArgumentException>(() => new AstronomyClassificationAssignment(default, value, AstronomyClassificationQualifier.Primary));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AstronomyClassificationAssignment(scheme, value, (AstronomyClassificationQualifier)999));
        Assert.Null(new AstronomyClassificationAssignment(scheme, value, AstronomyClassificationQualifier.Secondary, " ").Note);
        Assert.Equal(512, new AstronomyClassificationAssignment(scheme, value, AstronomyClassificationQualifier.Secondary, new string('n', 512)).Note!.Length);
        Assert.Throws<ArgumentException>(() => new AstronomyClassificationAssignment(scheme, value, AstronomyClassificationQualifier.Secondary, new string('n', 513)));
        Assert.Throws<ArgumentException>(() => new AstronomyClassificationAssignment(scheme, value, AstronomyClassificationQualifier.Secondary, "bad\n"));
        Assert.Equal(assignment, equivalent);
    }

    [Fact]
    public void Classification_payload_contract_is_complete()
    {
        var primary = Assignment("scheme-a", "code-a", AstronomyClassificationQualifier.Primary);
        var secondary = Assignment("scheme-b", "code-b", AstronomyClassificationQualifier.Secondary);
        var callerInput = new List<AstronomyClassificationAssignment> { secondary, primary };
        var payload = new AstronomyEntityClassificationPayload(ClassificationTypeId, AstronomyEntityKind.Star, callerInput);
        callerInput.Clear();

        Assert.Throws<ArgumentException>(() => new AstronomyEntityClassificationPayload(default, AstronomyEntityKind.Star, [primary]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AstronomyEntityClassificationPayload(ClassificationTypeId, (AstronomyEntityKind)999, [primary]));
        Assert.Throws<ArgumentNullException>(() => new AstronomyEntityClassificationPayload(ClassificationTypeId, AstronomyEntityKind.Star, null!));
        Assert.Throws<ArgumentException>(() => new AstronomyEntityClassificationPayload(ClassificationTypeId, AstronomyEntityKind.Star, []));
        Assert.Throws<ArgumentException>(() => new AstronomyEntityClassificationPayload(ClassificationTypeId, AstronomyEntityKind.Star, [primary, null!]));
        Assert.Equal(2, payload.Assignments.Count);
        Assert.IsAssignableFrom<ReadOnlyCollection<AstronomyClassificationAssignment>>(payload.Assignments);
        Assert.Equal(new[] { "scheme-a", "scheme-b" }, payload.Assignments.Select(assignment => assignment.SchemeId.Value));
        Assert.Equal(payload, new AstronomyEntityClassificationPayload(ClassificationTypeId, AstronomyEntityKind.Star, [secondary, primary]));
        Assert.Equal(payload.GetHashCode(), new AstronomyEntityClassificationPayload(ClassificationTypeId, AstronomyEntityKind.Star, [secondary, primary]).GetHashCode());
        AssertNoPublicState(typeof(AstronomyEntityClassificationPayload), "Evidence", "Confidence", "Audit", "Validity", "Source", "CurrentTime");
    }

    [Fact]
    public void Classification_payload_semantic_duplicate_rules_are_complete()
    {
        var exact = Assignment("scheme", "code", AstronomyClassificationQualifier.Secondary, note: "note");
        Assert.Throws<ArgumentException>(() => new AstronomyEntityClassificationPayload(ClassificationTypeId, AstronomyEntityKind.Star, [exact, exact]));
        Assert.Throws<ArgumentException>(() => new AstronomyEntityClassificationPayload(ClassificationTypeId, AstronomyEntityKind.Star, [exact, Assignment("scheme", "code", AstronomyClassificationQualifier.Secondary, note: "different note")]));
        Assert.Throws<ArgumentException>(() => new AstronomyEntityClassificationPayload(ClassificationTypeId, AstronomyEntityKind.Star, [exact, Assignment("scheme", "code", AstronomyClassificationQualifier.Secondary, displayName: "Different display")]));

        var qualified = new AstronomyEntityClassificationPayload(ClassificationTypeId, AstronomyEntityKind.Star, [
            Assignment("scheme", "code", AstronomyClassificationQualifier.Secondary),
            Assignment("scheme", "code", AstronomyClassificationQualifier.Alternative)]);
        Assert.Equal(2, qualified.Assignments.Count);

        Assert.Throws<ArgumentException>(() => new AstronomyEntityClassificationPayload(ClassificationTypeId, AstronomyEntityKind.Star, [
            Assignment("scheme", "code-a", AstronomyClassificationQualifier.Primary),
            Assignment("scheme", "code-b", AstronomyClassificationQualifier.Primary)]));

        var crossScheme = new AstronomyEntityClassificationPayload(ClassificationTypeId, AstronomyEntityKind.Star, [
            Assignment("scheme-a", "code", AstronomyClassificationQualifier.Primary),
            Assignment("scheme-b", "code", AstronomyClassificationQualifier.Primary)]);
        Assert.Equal(2, crossScheme.Assignments.Count);
    }

    [Fact]
    public void Physical_property_id_contract_is_complete()
    {
        var id = new AstronomyPhysicalPropertyId("  Physical.Radius  ");

        Assert.Equal("physical.radius", id.Value);
        Assert.Equal(new AstronomyPhysicalPropertyId("physical.radius"), id);
        Assert.False(default(AstronomyPhysicalPropertyId).IsValid);
        Assert.Equal("physical.radius", id.ToString());
        Assert.Throws<ArgumentException>(() => new AstronomyPhysicalPropertyId(" "));
        Assert.Throws<ArgumentException>(() => new AstronomyPhysicalPropertyId("bad token"));
        Assert.Throws<ArgumentException>(() => new AstronomyPhysicalPropertyId("bad\n"));
        Assert.Equal(128, new AstronomyPhysicalPropertyId(new string('A', 128)).Value.Length);
        Assert.Throws<ArgumentException>(() => new AstronomyPhysicalPropertyId(new string('a', 129)));
    }

    [Fact]
    public void Physical_enum_taxonomies_are_frozen_and_guarded()
    {
        Assert.Equal(new[] { "Size", "Mass", "Density", "Gravity", "Thermal", "Radiative", "Rotational", "Structural", "Surface", "Atmospheric", "Compositional", "Chronological", "Other" }, Enum.GetNames<AstronomyPhysicalPropertyCategory>());
        Assert.Equal(new[] { "Mean", "Minimum", "Maximum", "Equatorial", "Polar", "Effective", "Nominal", "Estimated", "ModelDerived", "Observed", "Reference" }, Enum.GetNames<AstronomyPhysicalPropertyQualifier>());
        Assert.Equal(new[] { "ScalarMeasurement", "MeasurementRange", "Text", "Boolean" }, Enum.GetNames<AstronomyPhysicalPropertyValueKind>());
        Assert.Equal(Enum.GetValues<AstronomyPhysicalPropertyCategory>().Length, Enum.GetValues<AstronomyPhysicalPropertyCategory>().Cast<int>().Distinct().Count());
        Assert.Equal(Enum.GetValues<AstronomyPhysicalPropertyQualifier>().Length, Enum.GetValues<AstronomyPhysicalPropertyQualifier>().Cast<int>().Distinct().Count());
        Assert.Equal(Enum.GetValues<AstronomyPhysicalPropertyValueKind>().Length, Enum.GetValues<AstronomyPhysicalPropertyValueKind>().Cast<int>().Distinct().Count());

        foreach (var category in Enum.GetValues<AstronomyPhysicalPropertyCategory>())
        {
            Assert.Equal(category, Property("physical." + category.ToString().ToLowerInvariant(), category).Category);
        }

        foreach (var qualifier in Enum.GetValues<AstronomyPhysicalPropertyQualifier>())
        {
            Assert.Equal(qualifier, Property("physical.radius", AstronomyPhysicalPropertyCategory.Size, qualifier).Qualifier);
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => Property("physical.radius", (AstronomyPhysicalPropertyCategory)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => Property("physical.radius", AstronomyPhysicalPropertyCategory.Size, (AstronomyPhysicalPropertyQualifier)999));
    }

    [Fact]
    public void Measurement_range_contract_is_complete()
    {
        var equal = new AstronomyMeasurementRange(new AstronomyMeasurement(1m, Kilometer), new AstronomyMeasurement(1m, Kilometer));
        var increasing = new AstronomyMeasurementRange(new AstronomyMeasurement(1m, Kilometer), new AstronomyMeasurement(2m, Kilometer));
        var negative = new AstronomyMeasurementRange(new AstronomyMeasurement(-2m, Kilometer), new AstronomyMeasurement(-1m, Kilometer));

        Assert.Equal(equal.Minimum, equal.Maximum);
        Assert.Equal(1m, increasing.Minimum.Value);
        Assert.Equal(-2m, negative.Minimum.Value);
        Assert.Throws<ArgumentNullException>(() => new AstronomyMeasurementRange(null!, new AstronomyMeasurement(1m, Kilometer)));
        Assert.Throws<ArgumentNullException>(() => new AstronomyMeasurementRange(new AstronomyMeasurement(1m, Kilometer), null!));
        Assert.Throws<ArgumentException>(() => new AstronomyMeasurementRange(new AstronomyMeasurement(2m, Kilometer), new AstronomyMeasurement(1m, Kilometer)));
        Assert.Throws<ArgumentException>(() => new AstronomyMeasurementRange(new AstronomyMeasurement(1m, Kilometer), new AstronomyMeasurement(2m, Meter)));
        Assert.Throws<ArgumentException>(() => new AstronomyMeasurementRange(new AstronomyMeasurement(1m, Kilometer), new AstronomyMeasurement(2m, Kilogram)));
        Assert.Equal(increasing, new AstronomyMeasurementRange(new AstronomyMeasurement(1m, Kilometer), new AstronomyMeasurement(2m, Kilometer)));
    }

    [Fact]
    public void Physical_value_variants_contract_is_complete_and_hierarchy_is_closed()
    {
        Assert.Throws<ArgumentNullException>(() => new AstronomyScalarPhysicalPropertyValue(null!));
        Assert.Throws<ArgumentNullException>(() => new AstronomyRangePhysicalPropertyValue(null!));
        Assert.Equal("Text", new AstronomyTextPhysicalPropertyValue(" Text ").Value);
        Assert.Throws<ArgumentException>(() => new AstronomyTextPhysicalPropertyValue(" "));
        Assert.Throws<ArgumentException>(() => new AstronomyTextPhysicalPropertyValue("bad\n"));
        Assert.Equal(512, new AstronomyTextPhysicalPropertyValue(new string('t', 512)).Value.Length);
        Assert.Throws<ArgumentException>(() => new AstronomyTextPhysicalPropertyValue(new string('t', 513)));
        Assert.True(new AstronomyBooleanPhysicalPropertyValue(true).Value);
        Assert.False(new AstronomyBooleanPhysicalPropertyValue(false).Value);
        Assert.Equal(AstronomyPhysicalPropertyValueKind.ScalarMeasurement, new AstronomyScalarPhysicalPropertyValue(new AstronomyMeasurement(1m, Kilometer)).Kind);
        Assert.Equal(AstronomyPhysicalPropertyValueKind.MeasurementRange, new AstronomyRangePhysicalPropertyValue(new AstronomyMeasurementRange(new AstronomyMeasurement(1m, Kilometer), new AstronomyMeasurement(2m, Kilometer))).Kind);
        Assert.Equal(AstronomyPhysicalPropertyValueKind.Text, new AstronomyTextPhysicalPropertyValue("x").Kind);
        Assert.Equal(AstronomyPhysicalPropertyValueKind.Boolean, new AstronomyBooleanPhysicalPropertyValue(true).Kind);

        var constructor = typeof(AstronomyPhysicalPropertyValue).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
        Assert.True(constructor.IsFamilyAndAssembly);
        var supportedVariants = typeof(AstronomyPhysicalPropertyValue).Assembly
            .GetTypes()
            .Where(type => type.BaseType == typeof(AstronomyPhysicalPropertyValue))
            .OrderBy(type => type.Name)
            .ToArray();
        var expectedVariants = new[]
            {
                typeof(AstronomyBooleanPhysicalPropertyValue),
                typeof(AstronomyRangePhysicalPropertyValue),
                typeof(AstronomyScalarPhysicalPropertyValue),
                typeof(AstronomyTextPhysicalPropertyValue)
            }
            .OrderBy(type => type.Name)
            .ToArray();
        Assert.Equal(expectedVariants, supportedVariants);
    }

    [Fact]
    public void Physical_property_contract_is_complete()
    {
        var property = Property("physical.radius", AstronomyPhysicalPropertyCategory.Size, AstronomyPhysicalPropertyQualifier.Mean, note: " note ");
        var equivalent = Property("physical.radius", AstronomyPhysicalPropertyCategory.Size, AstronomyPhysicalPropertyQualifier.Mean, note: "note");

        Assert.Throws<ArgumentException>(() => new AstronomyPhysicalProperty(default, AstronomyPhysicalPropertyCategory.Size, Scalar()));
        Assert.Throws<ArgumentOutOfRangeException>(() => Property("physical.radius", (AstronomyPhysicalPropertyCategory)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => Property("physical.radius", AstronomyPhysicalPropertyCategory.Size, (AstronomyPhysicalPropertyQualifier)999));
        Assert.Throws<ArgumentNullException>(() => new AstronomyPhysicalProperty(new("physical.radius"), AstronomyPhysicalPropertyCategory.Size, null!));
        Assert.Null(Property("physical.radius", AstronomyPhysicalPropertyCategory.Size, note: " ").Note);
        Assert.Equal(512, Property("physical.radius", AstronomyPhysicalPropertyCategory.Size, note: new string('n', 512)).Note!.Length);
        Assert.Throws<ArgumentException>(() => Property("physical.radius", AstronomyPhysicalPropertyCategory.Size, note: new string('n', 513)));
        Assert.Throws<ArgumentException>(() => Property("physical.radius", AstronomyPhysicalPropertyCategory.Size, note: "bad\n"));
        Assert.Equal(property, equivalent);
    }

    [Fact]
    public void Physical_payload_contract_is_complete()
    {
        var radius = Property("physical.radius", AstronomyPhysicalPropertyCategory.Size, AstronomyPhysicalPropertyQualifier.Mean);
        var mass = Property("physical.mass", AstronomyPhysicalPropertyCategory.Mass);
        var callerInput = new List<AstronomyPhysicalProperty> { radius, mass };
        var payload = new AstronomyPhysicalPropertiesPayload(PhysicalTypeId, callerInput);
        callerInput.Clear();

        Assert.Throws<ArgumentException>(() => new AstronomyPhysicalPropertiesPayload(default, [radius]));
        Assert.Throws<ArgumentNullException>(() => new AstronomyPhysicalPropertiesPayload(PhysicalTypeId, null!));
        Assert.Throws<ArgumentException>(() => new AstronomyPhysicalPropertiesPayload(PhysicalTypeId, []));
        Assert.Throws<ArgumentException>(() => new AstronomyPhysicalPropertiesPayload(PhysicalTypeId, [radius, null!]));
        Assert.Equal(2, payload.Properties.Count);
        Assert.IsAssignableFrom<ReadOnlyCollection<AstronomyPhysicalProperty>>(payload.Properties);
        Assert.Equal(new[] { "physical.radius", "physical.mass" }, payload.Properties.Select(property => property.PropertyId.Value));
        Assert.Throws<ArgumentException>(() => new AstronomyPhysicalPropertiesPayload(PhysicalTypeId, [radius, Property("physical.radius", AstronomyPhysicalPropertyCategory.Size, AstronomyPhysicalPropertyQualifier.Mean, Scalar(2m))]));
        Assert.Equal(2, new AstronomyPhysicalPropertiesPayload(PhysicalTypeId, [radius, Property("physical.radius", AstronomyPhysicalPropertyCategory.Size, AstronomyPhysicalPropertyQualifier.Polar)]).Properties.Count);
        Assert.Throws<ArgumentException>(() => new AstronomyPhysicalPropertiesPayload(PhysicalTypeId, [mass, Property("physical.mass", AstronomyPhysicalPropertyCategory.Mass, null, Scalar(2m))]));
        Assert.Equal(payload, new AstronomyPhysicalPropertiesPayload(PhysicalTypeId, [mass, radius]));
        Assert.Equal(payload.GetHashCode(), new AstronomyPhysicalPropertiesPayload(PhysicalTypeId, [mass, radius]).GetHashCode());
        AssertNoPublicState(typeof(AstronomyPhysicalPropertiesPayload), "Observer", "Coordinate", "Orbital", "Event", "Evidence", "Confidence", "Audit", "Validity");
    }

    [Fact]
    public void Task23B_architecture_forbidden_references_are_absent_from_focused_production_files()
    {
        var root = FindRepositoryRoot();
        var relativeFiles = Directory
            .EnumerateFiles(Path.Combine(root, "Backend/src/Astronomy.MediaFactory.Core/KnowledgeFoundation/TypedDomains/Classification"), "*.cs")
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "Backend/src/Astronomy.MediaFactory.Core/KnowledgeFoundation/TypedDomains/Physical"), "*.cs"))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var forbidden = new[] { "EvidenceId", "ConfidenceAssessmentId", "KnowledgeConfidenceLevel", "AstronomyObservationContext", "AstronomyReferenceFrame", "AstronomyReferenceOrigin", "AstronomyCoordinateSystem", "AstronomyEpochReference", "JsonConverter", "JsonSerializerOptions", "IServiceCollection", "DbContext", "IQueryable", "HttpClient", "Stellarium", "Skyfield", "SPICE", "DateTimeOffset.UtcNow", "CertificationCoordinator", "Infrastructure", "Persistence", "EntityFrameworkCore", "Publishing", "Rendering", "AIOptimization", "ContentGen", "Calculate", "Compute", "ConvertTo", "Infer" };

        Assert.All(relativeFiles, file =>
        {
            var text = File.ReadAllText(Path.Combine(root, file));
            foreach (var term in forbidden)
            {
                Assert.DoesNotContain(term, text, StringComparison.Ordinal);
            }
        });
    }

    private static AstronomyClassificationAssignment Assignment(string scheme, string code, AstronomyClassificationQualifier qualifier, string displayName = "Name", string? note = null)
    {
        return new AstronomyClassificationAssignment(new AstronomyClassificationSchemeId(scheme), new AstronomyClassificationValue(code, displayName), qualifier, note);
    }

    private static AstronomyScalarPhysicalPropertyValue Scalar(decimal value = 1m)
    {
        return new AstronomyScalarPhysicalPropertyValue(new AstronomyMeasurement(value, Kilometer));
    }

    private static AstronomyPhysicalProperty Property(string id, AstronomyPhysicalPropertyCategory category, AstronomyPhysicalPropertyQualifier? qualifier = null, AstronomyPhysicalPropertyValue? value = null, string? note = null)
    {
        return new AstronomyPhysicalProperty(new AstronomyPhysicalPropertyId(id), category, value ?? Scalar(), qualifier, note);
    }

    private static void AssertNoPublicState(Type type, params string[] forbiddenFragments)
    {
        Assert.All(type.GetProperties(BindingFlags.Instance | BindingFlags.Public), property =>
        {
            Assert.Null(property.SetMethod);
            foreach (var fragment in forbiddenFragments)
            {
                Assert.DoesNotContain(fragment, property.Name, StringComparison.OrdinalIgnoreCase);
            }
        });
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Backend/Astronomy.MediaFactory.slnx")))
        {
            current = current.Parent;
        }

        return current?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
