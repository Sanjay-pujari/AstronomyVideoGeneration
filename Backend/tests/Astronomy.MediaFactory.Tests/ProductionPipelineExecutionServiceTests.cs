using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;

namespace Astronomy.MediaFactory.Tests;

public sealed class ProductionPipelineExecutionServiceTests
{
    [Fact]
    public void FirstNonEmpty_ReturnsEmptyString_WhenAllCandidatesAreMissing()
    {
        var method = typeof(ProductionPipelineExecutionService).GetMethod("FirstNonEmpty", BindingFlags.NonPublic | BindingFlags.Static);

        var result = method!.Invoke(null, new object?[] { new string?[] { null, "", "   " } });

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Phase19CinematicDiagnostics_TrustsPhase18VideoDiagnostics()
    {
        var diagnostics = JsonNode.Parse(JsonSerializer.Serialize(new
        {
            cinematicOutroEnabled = true,
            cinematicOutroDurationSec = 4.0,
            fadeToBlackEnabled = true,
            fadeToBlackDurationSec = 1.0
        }));

        Assert.True(InvokePhase18DiagnosticsValidator("IsPhase18CinematicOutroValidated", diagnostics));
        Assert.True(InvokePhase18DiagnosticsValidator("IsPhase18FadeToBlackValidated", diagnostics));
    }

    [Fact]
    public void Phase19CinematicDiagnostics_RejectsInsufficientPhase18Durations()
    {
        var diagnostics = JsonNode.Parse(JsonSerializer.Serialize(new
        {
            cinematicOutroEnabled = true,
            cinematicOutroDurationSec = 3.99,
            fadeToBlackEnabled = true,
            fadeToBlackDurationSec = 0.99
        }));

        Assert.False(InvokePhase18DiagnosticsValidator("IsPhase18CinematicOutroValidated", diagnostics));
        Assert.False(InvokePhase18DiagnosticsValidator("IsPhase18FadeToBlackValidated", diagnostics));
    }

    [Fact]
    public void Phase16SubtitleRegeneration_UsesCueLevelTtsTimelineDurations()
    {
        var planRoot = Path.Combine(Path.GetTempPath(), "phase16-tts-srt-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(planRoot, "tts"));
            var timelinePath = Path.Combine(planRoot, "tts", "tts-timeline.json");
            File.WriteAllText(timelinePath, JsonSerializer.Serialize(new
            {
                @short = new
                {
                    items = new[]
                    {
                        new { format = "short", sceneId = "001", cueIndex = 1, cueText = "First cue.", audioDurationSec = 5.352 },
                        new { format = "short", sceneId = "002", cueIndex = 2, cueText = "Second cue.", audioDurationSec = 1.25 }
                    }
                },
                @long = new
                {
                    items = new[]
                    {
                        new { format = "long", sceneId = "001", cueIndex = 1, cueText = "Long first cue.", audioDurationSec = 2.0 }
                    }
                }
            }));

            var method = typeof(ProductionPipelineExecutionService).GetMethod("RegenerateNarrationSubtitlesFromTtsTimeline", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            method!.Invoke(null, [planRoot]);

            var shortSrt = File.ReadAllText(Path.Combine(planRoot, "narration", "subtitles", "short.srt"));
            Assert.Contains("00:00:00,000 --> 00:00:05,352", shortSrt);
            Assert.Contains("00:00:05,352 --> 00:00:06,602", shortSrt);
            Assert.Contains("First cue.", shortSrt);
            Assert.Contains("Second cue.", shortSrt);
        }
        finally
        {
            if (Directory.Exists(planRoot)) Directory.Delete(planRoot, true);
        }
    }

    [Fact]
    public void Phase18VisualDurations_GroupCueLevelTtsTimelineDurationsByScene()
    {
        var planRoot = Path.Combine(Path.GetTempPath(), "phase18-visual-duration-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(planRoot, "tts"));
            File.WriteAllText(Path.Combine(planRoot, "tts", "tts-timeline.json"), JsonSerializer.Serialize(new
            {
                @short = new
                {
                    items = new[]
                    {
                        new { format = "short", sceneId = "001-hook", cueIndex = 1, cueText = "First cue.", audioDurationSec = 5.352 },
                        new { format = "short", sceneId = "001-hook", cueIndex = 2, cueText = "Second cue.", audioDurationSec = 1.25 },
                        new { format = "short", sceneId = "002-cause", cueIndex = 3, cueText = "Third cue.", audioDurationSec = 7.0 }
                    }
                },
                @long = new
                {
                    items = new[]
                    {
                        new { format = "long", sceneId = "001", cueIndex = 1, cueText = "Long first cue.", audioDurationSec = 2.0 }
                    }
                }
            }));

            var method = typeof(ProductionPipelineExecutionService).GetMethod("BuildCueLevelSceneDurationsFromTtsTimeline", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            var durations = (IReadOnlyDictionary<string, double>)method!.Invoke(null, [planRoot, "short"])!;

            Assert.Equal(6.602, durations["001-hook"], 3);
            Assert.Equal(7.0, durations["002-cause"], 3);
        }
        finally
        {
            if (Directory.Exists(planRoot)) Directory.Delete(planRoot, true);
        }
    }

    [Fact]
    public void Phase18VisualAssembly_KeepsSceneStructureWhileExpandingSceneDurations()
    {
        var planRoot = Path.Combine(Path.GetTempPath(), "phase18-scene-structure-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(planRoot, "tts"));
            File.WriteAllText(Path.Combine(planRoot, "tts", "tts-timeline.json"), JsonSerializer.Serialize(new
            {
                @short = new
                {
                    items = new[]
                    {
                        new { format = "short", sceneId = "001-hook", cueIndex = 1, cueText = "First cue.", audioDurationSec = 5.352 },
                        new { format = "short", sceneId = "001-hook", cueIndex = 2, cueText = "Second cue.", audioDurationSec = 1.25 },
                        new { format = "short", sceneId = "002-cause", cueIndex = 3, cueText = "Third cue.", audioDurationSec = 7.0 }
                    }
                }
            }));

            var motionRoot = JsonNode.Parse(JsonSerializer.Serialize(new
            {
                motionVersion = "V2",
                @short = new
                {
                    items = new[]
                    {
                        new { sceneId = "1-hook", imagePath = Path.Combine(planRoot, "scene-assets-v3", "short", "001.png"), durationSec = 2.0 },
                        new { sceneId = "002-cause", imagePath = Path.Combine(planRoot, "scene-assets-v3", "short", "002.png"), durationSec = 3.0 }
                    }
                }
            }))!;
            var ttsRoot = JsonNode.Parse(File.ReadAllText(Path.Combine(planRoot, "tts", "tts-timeline.json")))!;
            var missingSceneImages = new List<string>();
            var missingAudioFiles = new List<string>();
            var oldPathUsageReasons = new List<string>();
            var method = typeof(ProductionPipelineExecutionService).GetMethod("ReadVideoAssemblyItems", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            var items = ((System.Collections.IEnumerable)method!.Invoke(null, [planRoot, motionRoot, ttsRoot, "short", 5, Array.Empty<string>(), missingSceneImages, missingAudioFiles, oldPathUsageReasons])!)
                .Cast<object>()
                .ToArray();

            Assert.Equal(2, items.Length);
            Assert.Equal(6.602, ReadSceneDuration(items[0]), 3);
            Assert.Equal(7.0, ReadSceneDuration(items[1]), 3);
        }
        finally
        {
            if (Directory.Exists(planRoot)) Directory.Delete(planRoot, true);
        }

        static double ReadSceneDuration(object item)
            => (double)item.GetType().GetProperty("SceneDurationSec")!.GetValue(item)!;
    }

    [Fact]
    public void Phase18VisualAssembly_MatchesNumericPrefixTtsSceneDurations()
    {
        var cueDurations = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["001"] = 6.602,
            ["002"] = 7.0,
            ["003"] = 9.5
        };

        var method = typeof(ProductionPipelineExecutionService).GetMethod("MatchCueLevelSceneDuration", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var match = method!.Invoke(null, [cueDurations, "001-hook", 2.0])!;

        Assert.Equal("001-hook", ReadString(match, "RenderSceneId"));
        Assert.Equal("001-hook", ReadString(match, "NormalizedRenderSceneId"));
        Assert.Equal("001", ReadString(match, "MatchedTtsSceneId"));
        Assert.Equal("NumericPrefix", ReadString(match, "MatchMode"));
        Assert.Equal(6.602, ReadDouble(match, "GroupedCueDurationSec"), 3);
        Assert.Equal(2.0, ReadDouble(match, "OriginalSceneDurationSec"), 3);
        Assert.Equal(6.602, ReadDouble(match, "ExpandedSceneDurationSec"), 3);

        static string? ReadString(object item, string propertyName)
            => (string?)item.GetType().GetProperty(propertyName)!.GetValue(item);

        static double ReadDouble(object item, string propertyName)
            => (double)item.GetType().GetProperty(propertyName)!.GetValue(item)!;
    }

    [Fact]
    public void Phase14SubtitleSegmentation_SplitsNarrationIntoReadableCues()
    {
        const string narration = "Tonight, look low in the western sky after sunset. Venus appears bright, Jupiter sits nearby, and the Moon gives you a simple landmark. Pause for a moment and let your eyes adjust before you scan again.";

        var splitMethod = typeof(ProductionPipelineExecutionService).GetMethod("SplitSubtitleChunks", BindingFlags.NonPublic | BindingFlags.Static);
        var wrapMethod = typeof(ProductionPipelineExecutionService).GetMethod("WrapSubtitleChunk", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(splitMethod);
        Assert.NotNull(wrapMethod);

        var chunks = (IReadOnlyList<string>)splitMethod!.Invoke(null, [narration])!;
        var wrapped = chunks.Select(chunk => (IReadOnlyList<string>)wrapMethod!.Invoke(null, [chunk])!).ToArray();

        Assert.True(chunks.Count > 1);
        Assert.Equal(narration, string.Join(" ", chunks));
        Assert.All(chunks, chunk => Assert.InRange(chunk.Length, 1, 84));
        Assert.All(wrapped, lines =>
        {
            Assert.InRange(lines.Count, 1, 2);
            Assert.All(lines, line => Assert.InRange(line.Length, 1, 42));
        });
    }

    [Fact]
    public void Phase14SubtitleSegmentation_DoesNotSplitInsideWords()
    {
        const string narration = "Tonight, turn your attention to the western horizon as a planetary conjunction gathers after sunset. Keep watching while Venus and Jupiter settle lower together.";

        var splitMethod = typeof(ProductionPipelineExecutionService).GetMethod("SplitSubtitleChunks", BindingFlags.NonPublic | BindingFlags.Static);
        var wrapMethod = typeof(ProductionPipelineExecutionService).GetMethod("WrapSubtitleChunk", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(splitMethod);
        Assert.NotNull(wrapMethod);

        var chunks = (IReadOnlyList<string>)splitMethod!.Invoke(null, [narration])!;
        var wrapped = chunks.Select(chunk => (IReadOnlyList<string>)wrapMethod!.Invoke(null, [chunk])!).ToArray();
        var reconstructed = string.Join(" ", chunks);
        var srtText = string.Join(" ", wrapped.SelectMany(lines => lines));

        Assert.Equal(narration, reconstructed);
        Assert.Equal(narration, srtText);
        Assert.DoesNotContain("turn y", srtText);
        Assert.DoesNotContain("our attention", srtText);
        Assert.DoesNotContain("planet ary", srtText);
        Assert.DoesNotContain("planet\nary", string.Join("\n", wrapped.SelectMany(lines => lines)));
        Assert.All(chunks, chunk => Assert.InRange(chunk.Length, 1, 84));
        Assert.All(wrapped, lines =>
        {
            Assert.InRange(lines.Count, 1, 2);
            Assert.All(lines, line => Assert.InRange(line.Length, 1, 42));
        });
    }

    [Fact]
    public void Phase14SubtitleSegmentation_SplitsLongPhrasesOnlyAtWhitespace()
    {
        const string narration = "Tonight, turn your attention carefully toward the western horizon while the planetary conjunction keeps glowing after sunset.";

        var splitMethod = typeof(ProductionPipelineExecutionService).GetMethod("SplitSubtitleChunks", BindingFlags.NonPublic | BindingFlags.Static);
        var wrapMethod = typeof(ProductionPipelineExecutionService).GetMethod("WrapSubtitleChunk", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(splitMethod);
        Assert.NotNull(wrapMethod);

        var chunks = (IReadOnlyList<string>)splitMethod!.Invoke(null, [narration])!;
        var wrapped = chunks.Select(chunk => (IReadOnlyList<string>)wrapMethod!.Invoke(null, [chunk])!).ToArray();
        var srtText = string.Join(" ", wrapped.SelectMany(lines => lines));

        Assert.Equal(narration, string.Join(" ", chunks));
        Assert.Equal(narration, srtText);
        Assert.DoesNotContain("turn y", srtText);
        Assert.DoesNotContain("planet ary", srtText);
        Assert.All(chunks, chunk => Assert.InRange(chunk.Length, 1, 84));
        Assert.All(wrapped, lines =>
        {
            Assert.InRange(lines.Count, 1, 2);
            Assert.All(lines, line => Assert.InRange(line.Length, 1, 42));
        });
    }

    [Fact]
    public void Phase18MotionV2Strength_RequestExperimentalDoesNotOverrideDefaultPlan()
    {
        Assert.Equal("Default", InvokePhase18MotionV2StrengthResolver("Experimental", "Default"));
    }

    [Fact]
    public void Phase18MotionV2Strength_DetectsRequestExperimentalDefaultDiagnosticsMismatch()
    {
        Assert.True(InvokePhase18MotionV2StrengthMismatch("Experimental", "Default"));
        Assert.False(InvokePhase18MotionV2StrengthMismatch("Experimental", "Experimental"));
        Assert.False(InvokePhase18MotionV2StrengthMismatch(null, "Default"));
    }

    [Fact]
    public void Phase18MotionV2Strength_UsesPlanBeforeDefaultWhenRequestIsNotExperimental()
    {
        Assert.Equal("Experimental", InvokePhase18MotionV2StrengthResolver(null, "Experimental"));
        Assert.Equal("Default", InvokePhase18MotionV2StrengthResolver(null, null));
    }

    [Fact]
    public void Phase18MotionV2Strength_WarnsWhenRequestOverridesDefaultPlan()
    {
        Assert.True(InvokePhase18MotionV2StrengthOverrideWarning("Experimental", "Default"));
        Assert.False(InvokePhase18MotionV2StrengthOverrideWarning("Experimental", "Experimental"));
        Assert.False(InvokePhase18MotionV2StrengthOverrideWarning(null, "Default"));
    }

    [Fact]
    public void PhaseGating_NamedFullMoonShortOnly_SkipsLongNarration()
    {
        var context = CreateContext("NamedFullMoon", ["ShortVideo"]);

        Assert.True(IsPhaseRequired(context, 13));
        Assert.True(IsPhaseRequired(context, 14));
        Assert.False(IsPhaseRequired(context, 15));
        Assert.True(IsPhaseRequired(context, 16));
        Assert.False(IsPhaseRequired(context, 17));
        Assert.True(IsPhaseRequired(context, 18));
        Assert.False(IsPhaseRequired(context, 19));
    }

    [Fact]
    public void PhaseGating_MeteorShortAndLong_RunsBothNarrationPhases()
    {
        var context = CreateContext("MeteorShower", ["ShortVideo", "LongVideo"]);

        Assert.True(IsPhaseRequired(context, 13));
        Assert.True(IsPhaseRequired(context, 14));
        Assert.True(IsPhaseRequired(context, 15));
        Assert.True(IsPhaseRequired(context, 16));
        Assert.True(IsPhaseRequired(context, 17));
        Assert.True(IsPhaseRequired(context, 18));
        Assert.True(IsPhaseRequired(context, 19));
    }

    [Fact]
    public void PhaseGating_ThumbnailOnly_RunsSceneAudioSyncButSkipsVideoPhasesNotRequested()
    {
        var context = CreateContext("FutureDomain", ["Thumbnail"]);

        Assert.False(IsPhaseRequired(context, 11));
        Assert.True(IsPhaseRequired(context, 12));
        Assert.True(IsPhaseRequired(context, 13));
        Assert.True(IsPhaseRequired(context, 14));
        Assert.False(IsPhaseRequired(context, 15));
        Assert.False(IsPhaseRequired(context, 16));
        Assert.False(IsPhaseRequired(context, 17));
        Assert.False(IsPhaseRequired(context, 18));
        Assert.False(IsPhaseRequired(context, 19));
        Assert.True(IsPhaseRequired(context, 20));
    }


    [Fact]
    public void Phase14NarrationExtraction_ReadsSectionsFromRootScenesArray()
    {
        var path = Path.Combine(Path.GetTempPath(), "astro-phase14-narration", Guid.NewGuid().ToString("N"), "question-driven-narration-v2.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            section = "WrongRootSection",
            narration = new { section = "WrongNarrationSection" },
            scenes = new[]
            {
                new { sceneNumber = 1, section = "Hook", narrationText = "Look up tonight." },
                new { sceneNumber = 2, section = "ViewingAdvice", narrationText = "Face west after sunset." },
                new { sceneNumber = 3, section = "Explanation", narrationText = "The alignment is easy to see." },
                new { sceneNumber = 4, section = "Reward", narrationText = "You will spot a bright pairing." },
                new { sceneNumber = 5, section = "Curiosity", narrationText = "The planets only appear close." },
                new { sceneNumber = 6, section = "CTA", narrationText = "Save this reminder." }
            }
        }));

        var method = typeof(ProductionPipelineExecutionService).GetMethod("ExtractNarrationBeats", BindingFlags.NonPublic | BindingFlags.Static);
        var beats = Assert.IsAssignableFrom<System.Collections.IEnumerable>(method!.Invoke(null, [path]));
        var sections = beats.Cast<object>()
            .Select(beat => beat.GetType().GetProperty("Section")!.GetValue(beat)?.ToString())
            .ToArray();

        Assert.Equal(["Hook", "ViewingAdvice", "Explanation", "Reward", "Curiosity", "CTA"], sections);
        Assert.DoesNotContain("WrongRootSection", sections);
        Assert.DoesNotContain("WrongNarrationSection", sections);
    }

    [Fact]
    public void Phase14DocumentaryNarration_PlanetConjunctionUsesDocumentaryStoryArcAndPerspective()
    {
        var context = CreateContext("PlanetConjunction", ["ShortVideo", "LongVideo"], "Venus Jupiter Conjunction");
        var method = typeof(ProductionPipelineExecutionService).GetMethod("BuildPhase14DocumentaryNarration", BindingFlags.NonPublic | BindingFlags.Static);

        var narration = method!.Invoke(null, [context])!;
        var narrationType = narration.GetType();
        var shortItems = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(narrationType.GetProperty("ShortItems")!.GetValue(narration));
        var longItems = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(narrationType.GetProperty("LongItems")!.GetValue(narration));
        var diagnostics = narrationType.GetProperty("Diagnostics")!.GetValue(narration)!;
        var diagnosticsType = diagnostics.GetType();
        var allText = string.Join(" ", shortItems.Values.Concat(longItems.Values));

        Assert.Equal(["001-hook", "002-what-is-it", "003-cause", "004-viewing-tip", "005-final-reminder"], shortItems.Keys.ToArray());
        Assert.Equal(9, longItems.Count);
        Assert.StartsWith("Hello, fellow stargazers.", shortItems["001-hook"]);
        Assert.Contains("Over the next few evenings", shortItems["001-hook"], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Let’s take a closer look.", shortItems["001-hook"], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("planetary conjunction", allText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("appear close together", allText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hundreds of millions of kilometers", allText, StringComparison.OrdinalIgnoreCase);
        Assert.Matches("separated|distances|space|line-of-sight|perspective", allText);
        Assert.DoesNotContain("low in the evening sky", allText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("start with", allText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("you will see", allText, StringComparison.OrdinalIgnoreCase);
        Assert.True((int)diagnosticsType.GetProperty("DocumentaryScore")!.GetValue(diagnostics)! >= 90);
        Assert.True((int)diagnosticsType.GetProperty("WonderScore")!.GetValue(diagnostics)! >= 90);
        Assert.True((int)diagnosticsType.GetProperty("ScientificAccuracyScore")!.GetValue(diagnostics)! >= 95);
    }

    [Fact]
    public void RequestedOutputCompletion_ReportsSkippedForUnrequestedLongVideo()
    {
        var context = CreateContext("PlanetPairing", ["ShortVideo", "Thumbnail"]);
        var now = DateTimeOffset.UtcNow;
        ProductionPhaseResult[] phaseResults =
        [
            new(12, "Generate Thumbnails", ProductionPhaseStatus.Succeeded, now, now, 0, [], [], null, [], [], false),
            new(13, "Generate Gallery", ProductionPhaseStatus.Succeeded, now, now, 0, [], [], null, [], [], false),
            new(14, "Scene Audio Sync V1", ProductionPhaseStatus.Succeeded, now, now, 0, [], [], null, [], [], false),
            new(15, "Generate Long Narration", ProductionPhaseStatus.Skipped, now, now, 0, [], [], null, [], [], false, "Output type not requested"),
            new(16, "Generate Short TTS", ProductionPhaseStatus.Succeeded, now, now, 0, [], [], null, [], [], false),
            new(17, "Motion Layer V1", ProductionPhaseStatus.Succeeded, now, now, 0, [], [], null, [], [], false),
            new(18, "Assemble Short Video", ProductionPhaseStatus.Succeeded, now, now, 0, [], [], null, [], [], false),
            new(19, "Assemble Long Video", ProductionPhaseStatus.Skipped, now, now, 0, [], [], null, [], [], false, "Output type not requested")
        ];

        var completion = BuildRequestedOutputCompletion(context, phaseResults);

        Assert.Contains(completion, item => item.OutputType == "ShortVideo" && item.Requested && item.Status == "Succeeded");
        Assert.Contains(completion, item => item.OutputType == "LongVideo" && !item.Requested && item.Status == "Skipped");
        Assert.Contains(completion, item => item.OutputType == "Thumbnail" && item.Requested && item.Status == "Succeeded");
    }

    [Fact]
    public void Phase10SceneAssetDiagnostics_CountsV2SceneAssetFinalPngs()
    {
        var root = Path.Combine(Path.GetTempPath(), "astro-phase10-scene-assets", Guid.NewGuid().ToString("N"), "scene-approval-v3");
        WritePhase10SceneAssets(root, "short", 6);
        WritePhase10SceneAssets(root, "long", 6);

        var method = typeof(ProductionPipelineExecutionService)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(m => m.Name == "BuildPhase10SceneAssetDiagnostics"
                && m.GetParameters().Length == 1
                && m.GetParameters()[0].ParameterType == typeof(string));
        var diagnostics = method!.Invoke(null, [root])!;
        var diagnosticsType = diagnostics.GetType();

        Assert.Equal(6, diagnosticsType.GetProperty("ShortSceneCount")!.GetValue(diagnostics));
        Assert.Equal(6, diagnosticsType.GetProperty("LongSceneCount")!.GetValue(diagnostics));
        Assert.Equal(6, diagnosticsType.GetProperty("ShortPngCount")!.GetValue(diagnostics));
        Assert.Equal(6, diagnosticsType.GetProperty("LongPngCount")!.GetValue(diagnostics));
        Assert.Equal(false, diagnosticsType.GetProperty("LegacyArtifactCheckUsed")!.GetValue(diagnostics));
        Assert.Equal(true, diagnosticsType.GetProperty("V2ArtifactCheckUsed")!.GetValue(diagnostics));

        var validatedShortFinalPaths = Assert.IsAssignableFrom<IReadOnlyList<string>>(diagnosticsType.GetProperty("ValidatedShortFinalPaths")!.GetValue(diagnostics));
        var validatedLongFinalPaths = Assert.IsAssignableFrom<IReadOnlyList<string>>(diagnosticsType.GetProperty("ValidatedLongFinalPaths")!.GetValue(diagnostics));
        var missingFinalPaths = Assert.IsAssignableFrom<IReadOnlyList<string>>(diagnosticsType.GetProperty("MissingFinalPaths")!.GetValue(diagnostics));
        Assert.Equal(6, validatedShortFinalPaths.Count);
        Assert.Equal(6, validatedLongFinalPaths.Count);
        Assert.Empty(missingFinalPaths);
        Assert.Contains("scene-assets/short/scene-001/scene-001-final.png", validatedShortFinalPaths[0]);
        Assert.Contains("scene-assets/long/scene-001/scene-001-final.png", validatedLongFinalPaths[0]);
    }

    [Fact]
    public void Phase10SceneAssetValidation_RequiresFinalPngInEachV2SceneDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "astro-phase10-scene-assets", Guid.NewGuid().ToString("N"), "scene-approval-v3");
        WritePhase10SceneAssets(root, "short", 6);
        WritePhase10SceneAssets(root, "long", 6, skipFinalSceneNumber: 3);

        var method = typeof(ProductionPipelineExecutionService).GetMethod("ValidatePhase10SceneAssetCoverage", BindingFlags.NonPublic | BindingFlags.Static);
        var exception = Assert.Throws<TargetInvocationException>(() => method!.Invoke(null, [root]));

        var inner = Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Contains("long scene asset validation expected 6 final PNGs but found 5", inner.Message);
        Assert.Contains("scene-003-final.png", inner.Message);
    }

    [Fact]
    public void Phase10SceneAssetValidation_PassesWithV2FinalPngsAndNoLegacyFlatArtifacts()
    {
        var root = Path.Combine(Path.GetTempPath(), "astro-phase10-scene-assets", Guid.NewGuid().ToString("N"), "scene-approval-v3");
        WritePhase10SceneAssets(root, "short", 6);
        WritePhase10SceneAssets(root, "long", 6);

        var method = typeof(ProductionPipelineExecutionService).GetMethod("ValidatePhase10SceneAssetCoverage", BindingFlags.NonPublic | BindingFlags.Static);
        var exception = Record.Exception(() => method!.Invoke(null, [root]));

        Assert.Null(exception);
        Assert.False(File.Exists(Path.Combine(root, "short", "scene-001-final.png")));
        Assert.False(File.Exists(Path.Combine(root, "long", "scene-001-final.png")));
    }


    [Theory]
    [InlineData("MeteorShower", "Perseids Tonight", "MeteorShower")]
    [InlineData("PlanetPairing", "Venus Jupiter Pairing", "PlanetPairing")]
    [InlineData("Comet", "Comet Tonight", "Comet")]
    [InlineData("Eclipse", "Eclipse Tonight", "Eclipse")]
    public void BuildDurationTargetedShortNarration_UsesDynamicFacts_AndTargetsProfileRange(string eventType, string shortTitle, string expectedEventType)
    {
        var context = CreateContext(eventType, ["ShortVideo"], shortTitle);
        var buildMethod = typeof(ProductionPipelineExecutionService).GetMethod("BuildDurationTargetedShortNarration", BindingFlags.NonPublic | BindingFlags.Static);
        var estimateMethod = typeof(ProductionPipelineExecutionService).GetMethod("EstimateShortNarrationSeconds", BindingFlags.NonPublic | BindingFlags.Static);

        var narration = (string)buildMethod!.Invoke(null, [context])!;
        var estimatedSeconds = (double)estimateMethod!.Invoke(null, [narration])!;

        Assert.Contains(expectedEventType, narration);
        Assert.Contains(shortTitle, narration);
        Assert.Contains("western sky", narration);
        Assert.Contains("9 PM", narration);
        Assert.Contains("check clouds", narration, StringComparison.OrdinalIgnoreCase);
        Assert.InRange(estimatedSeconds, 30.0, 40.0);
    }

    [Fact]
    public void TrimLowestPriorityShortNarrationSentences_SelfCorrectsOneWordAndHalfSecondOverflow()
    {
        var context = CreateContext("MeteorShower", ["ShortVideo"], "Perseids Tonight");
        var trimMethod = typeof(ProductionPipelineExecutionService).GetMethod("TrimLowestPriorityShortNarrationSentences", BindingFlags.NonPublic | BindingFlags.Static);
        var countMethod = typeof(ProductionPipelineExecutionService).GetMethod("CountSpokenWords", BindingFlags.NonPublic | BindingFlags.Static);
        var estimateMethod = typeof(ProductionPipelineExecutionService).GetMethod("EstimateShortNarrationSeconds", BindingFlags.NonPublic | BindingFlags.Static);
        var narration = string.Join(" ", new[]
        {
            "Current MeteorShower Event makes Perseids Tonight worth planning for tonight with family nearby.",
            "Watch near western sky with peak timing around 9 PM and the best viewing window at 9 PM to midnight.",
            "Use a chair, dim your phone, and let your eyes adapt before scanning slowly.",
            "This extra context adds atmosphere, expectation, wonder, patience, comfort, curiosity, and perspective for viewers tonight.",
            "Check clouds, choose a safe open spot, save this viewing window, share it nearby, and step outside safely."
        });

        var preTrimWordCount = (int)countMethod!.Invoke(null, [narration])!;
        var preTrimDuration = (double)estimateMethod!.Invoke(null, [narration])!;
        var trimmed = (string)trimMethod!.Invoke(null, [narration, context])!;
        var postTrimWordCount = (int)countMethod.Invoke(null, [trimmed])!;
        var postTrimDuration = (double)estimateMethod.Invoke(null, [trimmed])!;

        Assert.Equal(80, preTrimWordCount);
        Assert.True(preTrimDuration > 45.0);
        Assert.True(postTrimWordCount <= 79);
        Assert.True(postTrimDuration <= 45.0);
        Assert.DoesNotContain("This extra context adds atmosphere", trimmed);
        Assert.Contains("Perseids Tonight", trimmed);
        Assert.Contains("9 PM to midnight", trimmed);
        Assert.Contains("Check clouds", trimmed);
    }


    [Fact]
    public void BuildPhase6SceneVisualVariants_ReturnsPlanningOnlyMetadataWithoutRendering()
    {
        var method = typeof(ProductionPipelineExecutionService).GetMethod("BuildPhase6SceneVisualVariants", BindingFlags.NonPublic | BindingFlags.Static);
        var scene = new EnrichedQuestionSceneDto(
            2,
            "How",
            "ExplainObject",
            "How do I find Mars?",
            "Look west after sunset.",
            "CasualSkyWatcher",
            "Beginner",
            "Mars is low in the west.",
            "Explain where Mars appears.",
            "Show Mars above the western horizon.",
            "Mars over a dim western horizon.",
            "Mars • western horizon",
            "Mars label near the horizon.",
            true);

        var variants = (IReadOnlyList<SceneVisualVariantDto>)method!.Invoke(null, [scene])!;

        Assert.InRange(variants.Count, 3, 5);
        Assert.Equal(["wide_context", "object_focus", "educational_overlay", "cinematic_detail", "transition_or_closing"], variants.Select(v => v.VariantType).ToArray());
        Assert.Equal(Enumerable.Range(1, variants.Count), variants.Select(v => v.VariantNo));
        Assert.Equal(variants.Count, variants.Select(v => v.CompositionHint).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(variants, variant => variant.VariantType == "wide_context" && variant.CompositionHint.Contains("WIDE FRAMING", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(variants, variant => variant.VariantType == "object_focus" && variant.CompositionHint.Contains("ZOOMED FRAMING", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(variants, variant => variant.VariantType == "educational_overlay" && variant.CompositionHint.Contains("INFOGRAPHIC LAYOUT", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(variants, variant => variant.VariantType == "cinematic_detail" && variant.CompositionHint.Contains("CLOSE-UP CINEMATIC", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(variants, variant => variant.VariantType == "transition_or_closing" && variant.CompositionHint.Contains("CTA COMPOSITION", StringComparison.OrdinalIgnoreCase));
        Assert.All(variants, variant =>
        {
            Assert.False(string.IsNullOrWhiteSpace(variant.Purpose));
            Assert.True(variant.RecommendedDurationSeconds > 0);
            Assert.False(string.IsNullOrWhiteSpace(variant.CameraStyle));
            Assert.False(string.IsNullOrWhiteSpace(variant.CompositionHint));
            Assert.False(string.IsNullOrWhiteSpace(variant.MotionHint));
            Assert.False(string.IsNullOrWhiteSpace(variant.OverlayHint));
            Assert.Contains("do not render", variant.RendererHint, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith("scene-02-", variant.OutputFileNameSuggestion, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void EnrichedSceneJson_OmitsVisualVariants_WhenSceneVariantsAreDisabled()
    {
        var scene = new EnrichedQuestionSceneDto(
            1,
            "What",
            "OpeningOverview",
            "What is happening?",
            "The Moon is full.",
            "CasualSkyWatcher",
            "Beginner",
            "The full Moon is visible tonight.",
            "Explain the full Moon timing.",
            "Show the Moon over the horizon.",
            "Full Moon above trees.",
            "Full Moon",
            "Moon label centered.",
            true);

        var json = JsonSerializer.Serialize(scene, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.DoesNotContain("visualVariants", json);
    }

    [Fact]
    public async Task Phase6SceneVisualVariants_AreWrittenIntoEnrichedScenePlan_WhenEnabled()
    {
        var context = CreateContext("NamedFullMoon", ["ShortVideo"], enableSceneVariants: true);
        await WriteEnrichedScenePlanAsync(context,
            viewerTakeaway: "The Moon is full tonight.",
            narrationIntent: "Explain when the full Moon rises.",
            visualIntent: "Show the Moon above the horizon.",
            imagePromptIntent: "Full Moon over a clean horizon.",
            overlayIntent: "Moon • eastern horizon",
            accessibilityIntent: "Full Moon label near the horizon.");
        var path = Path.Combine(context.ExecutionContext.QuestionRoot!, "question-driven-scene-plan.enriched.json");
        var method = typeof(ProductionPipelineExecutionService).GetMethod("AddPhase6SceneVisualVariantsAsync", BindingFlags.NonPublic | BindingFlags.Static);

        var generatedVariants = await (Task<int>)method!.Invoke(null, [path, CancellationToken.None])!;

        var json = await File.ReadAllTextAsync(path);
        var plan = JsonSerializer.Deserialize<EnrichedQuestionScenePlanDto>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.True(generatedVariants >= 3);
        Assert.Contains("visualVariants", json);
        Assert.All(plan.Scenes, scene => Assert.True(scene.VisualVariants?.Count >= 3));
    }

    [Fact]
    public async Task ValidatePhase6EnrichedScenePlanContract_Fails_WhenSceneVariantsEnabledAndAnySceneHasFewerThanThreeVariants()
    {
        var context = CreateContext("NamedFullMoon", ["ShortVideo"], enableSceneVariants: true);
        await WriteEnrichedScenePlanAsync(context,
            viewerTakeaway: "The Moon is full tonight.",
            narrationIntent: "Explain when the full Moon rises.",
            visualIntent: "Show the Moon above the horizon.",
            imagePromptIntent: "Full Moon over a clean horizon.",
            overlayIntent: "Moon • eastern horizon",
            accessibilityIntent: "Full Moon label near the horizon.");
        var path = Path.Combine(context.ExecutionContext.QuestionRoot!, "question-driven-scene-plan.enriched.json");
        var method = typeof(ProductionPipelineExecutionService).GetMethod("ValidatePhase6EnrichedScenePlanContractAsync", BindingFlags.NonPublic | BindingFlags.Static);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await (Task)method!.Invoke(null, [context, path, CancellationToken.None])!);

        Assert.Contains("at least 3 visual variants", exception.Message);
    }

    [Fact]
    public async Task Phase6PlanetGroupingDiagnostics_DetectsInjectedIntentPhrasesAcrossAllIntentFields()
    {
        var context = CreateContext("PLANET_GROUPING", ["ShortVideo"]);
        await WriteEnrichedScenePlanAsync(context,
            viewerTakeaway: "Planet grouping: show Saturn, Mars, Jupiter, and Venus in one viewing region.",
            narrationIntent: "Explain the bright planets from the horizon upward.",
            visualIntent: "Draw a grouping arc connecting the visible planets.",
            imagePromptIntent: "Show a quiet western horizon with labeled planets.",
            overlayIntent: "Saturn • Mars • Jupiter • Venus",
            accessibilityIntent: "Guided scan path: begin at western horizon and move upward.");

        var diagnostics = BuildPhase6SceneEnrichmentDiagnostics(context);

        Assert.True(GetBooleanDiagnostic(diagnostics, "PlanetGroupingIntentInjected"));
        Assert.True(GetBooleanDiagnostic(diagnostics, "GuidedScanPathInjected"));
        Assert.False(GetBooleanDiagnostic(diagnostics, "LegacyValidationPathExecuted"));
        Assert.True(GetBooleanDiagnostic(diagnostics, "PlanetGroupingValidationPathExecuted"));
        Assert.Equal(6, GetIntDiagnostic(diagnostics, "EnrichedSceneIntentCount"));
    }

    [Fact]
    public async Task Phase6PlanetGroupingDiagnostics_DoesNotApplyInjectedPhraseDetectionToOtherEventTypes()
    {
        var context = CreateContext("PlanetPairing", ["ShortVideo"]);
        await WriteEnrichedScenePlanAsync(context,
            viewerTakeaway: "Multi-planet grouping: show Saturn, Mars, Jupiter, and Venus in one viewing region.",
            narrationIntent: "Explain the bright planets from the horizon upward.",
            visualIntent: "Use a scan path from west to east.",
            imagePromptIntent: "Show a quiet western horizon with labeled planets.",
            overlayIntent: "Saturn • Mars • Jupiter • Venus",
            accessibilityIntent: "Guided scan path: begin at western horizon and move upward.");

        var diagnostics = BuildPhase6SceneEnrichmentDiagnostics(context);

        Assert.False(GetBooleanDiagnostic(diagnostics, "PlanetGroupingIntentInjected"));
        Assert.False(GetBooleanDiagnostic(diagnostics, "GuidedScanPathInjected"));
        Assert.True(GetBooleanDiagnostic(diagnostics, "LegacyValidationPathExecuted"));
        Assert.False(GetBooleanDiagnostic(diagnostics, "PlanetGroupingValidationPathExecuted"));
    }

    [Fact]
    public async Task Phase6PlanetGroupingContract_UsesInjectedIntentDiagnosticsInsteadOfLegacyObjectPresence()
    {
        var context = CreateContext("PLANET_GROUPING", ["ShortVideo"]);
        context = context with
        {
            ProductionEventIntelligence = context.ProductionEventIntelligence with
            {
                RequiredVisualObjects = ["planet grouping", "guided scan path"]
            }
        };
        await WriteEnrichedScenePlanAsync(context,
            viewerTakeaway: "Planet grouping: show Saturn, Mars, Jupiter, and Venus in one viewing region.",
            narrationIntent: "Explain the bright planets from the horizon upward.",
            visualIntent: "Draw a grouping arc connecting the visible planets.",
            imagePromptIntent: "Show a quiet western horizon with labeled planets.",
            overlayIntent: "Saturn • Mars • Jupiter • Venus",
            accessibilityIntent: "Begin at the western horizon and move upward.");

        await ValidatePhase6EnrichedScenePlanContractAsync(context);

        var diagnostics = BuildPhase6SceneEnrichmentDiagnostics(context);
        Assert.True(GetBooleanDiagnostic(diagnostics, "PlanetGroupingIntentInjected"));
        Assert.True(GetBooleanDiagnostic(diagnostics, "GuidedScanPathInjected"));
        Assert.False(GetBooleanDiagnostic(diagnostics, "LegacyValidationPathExecuted"));
        Assert.True(GetBooleanDiagnostic(diagnostics, "PlanetGroupingValidationPathExecuted"));
    }


    [Fact]
    public void Phase7NarrationValidation_Fails_WhenRequiredLegacyFilesAreMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "astro-pulse-phase7-validation-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var narrationPath = Path.Combine(root, "question-driven-narration.json");
        var reviewPath = Path.Combine(root, "question-driven-narration-review.json");
        var response = BuildValidNarrationResponse([]);
        var method = typeof(ProductionPipelineExecutionService).GetMethod("ValidatePhase7NarrationFilesGenerated", BindingFlags.NonPublic | BindingFlags.Static);

        var exception = Assert.Throws<TargetInvocationException>(() => method!.Invoke(null, [response, narrationPath, reviewPath]));

        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Contains("question-driven-narration.json", exception.InnerException!.Message);
        Assert.Contains("question-driven-narration-review.json", exception.InnerException.Message);
    }

    [Fact]
    public async Task Phase7NarrationValidation_Passes_WhenRequiredLegacyFilesExist()
    {
        var root = Path.Combine(Path.GetTempPath(), "astro-pulse-phase7-validation-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var narrationPath = Path.Combine(root, "question-driven-narration.json");
        var reviewPath = Path.Combine(root, "question-driven-narration-review.json");
        await File.WriteAllTextAsync(narrationPath, "{}");
        await File.WriteAllTextAsync(reviewPath, "{}");
        var response = BuildValidNarrationResponse([narrationPath, reviewPath]);
        var method = typeof(ProductionPipelineExecutionService).GetMethod("ValidatePhase7NarrationFilesGenerated", BindingFlags.NonPublic | BindingFlags.Static);

        method!.Invoke(null, [response, narrationPath, reviewPath]);
    }

    private static bool IsPhaseRequired(ProductionPhaseContext context, int phaseNo)
    {
        var method = typeof(ProductionPipelineExecutionService).GetMethod("IsPhaseRequiredForRequestedOutputs", BindingFlags.NonPublic | BindingFlags.Static);
        return (bool)method!.Invoke(null, [context, phaseNo])!;
    }

    private static object BuildPhase6SceneEnrichmentDiagnostics(ProductionPhaseContext context)
    {
        var method = typeof(ProductionPipelineExecutionService).GetMethod("BuildPhase6SceneEnrichmentDiagnostics", BindingFlags.NonPublic | BindingFlags.Static);
        return method!.Invoke(null, [context])!;
    }

    private static async Task ValidatePhase6EnrichedScenePlanContractAsync(ProductionPhaseContext context)
    {
        var method = typeof(ProductionPipelineExecutionService).GetMethod("ValidatePhase6EnrichedScenePlanContractAsync", BindingFlags.NonPublic | BindingFlags.Static);
        var path = Path.Combine(context.ExecutionContext.QuestionRoot!, "question-driven-scene-plan.enriched.json");
        var task = (Task)method!.Invoke(null, [context, path, CancellationToken.None])!;
        await task;
    }

    private static bool GetBooleanDiagnostic(object diagnostics, string propertyName)
        => (bool)diagnostics.GetType().GetProperty(propertyName)!.GetValue(diagnostics)!;

    private static int GetIntDiagnostic(object diagnostics, string propertyName)
        => (int)diagnostics.GetType().GetProperty(propertyName)!.GetValue(diagnostics)!;


    private static QuestionDrivenNarrationResponse BuildValidNarrationResponse(IReadOnlyList<string> generatedFiles)
    {
        var narration = new QuestionDrivenNarrationDto("event-id", "us", "en", [], 0, DateTimeOffset.UtcNow);
        var review = new QuestionDrivenNarrationReviewDto("event-id", "us", "en", true, 0, 0, [], [], DateTimeOffset.UtcNow);
        return new QuestionDrivenNarrationResponse("event-id", 0, 0, true, narration, review, generatedFiles, []);
    }

    private static async Task WriteEnrichedScenePlanAsync(
        ProductionPhaseContext context,
        string viewerTakeaway,
        string narrationIntent,
        string visualIntent,
        string imagePromptIntent,
        string overlayIntent,
        string accessibilityIntent)
    {
        Directory.CreateDirectory(context.ExecutionContext.QuestionRoot!);
        var plan = new EnrichedQuestionScenePlanDto(
            "event-id",
            context.Request.RegionId,
            context.Request.Language,
            "CasualSkyWatcher",
            "Beginner",
            [
                new EnrichedQuestionSceneDto(
                    1,
                    "What",
                    "OpeningOverview",
                    "What should I look for?",
                    "Look for the planets near the horizon.",
                    "CasualSkyWatcher",
                    "Beginner",
                    viewerTakeaway,
                    narrationIntent,
                    visualIntent,
                    imagePromptIntent,
                    overlayIntent,
                    accessibilityIntent,
                    true)
            ],
            true,
            DateTimeOffset.UtcNow,
            new QuestionSceneEnrichmentDiagnostics(
                context.ProductionEventIntelligence.EventType,
                context.ProductionEventIntelligence.RequiredVisualObjects,
                [],
                [],
                [],
                "Test"));
        var path = Path.Combine(context.ExecutionContext.QuestionRoot!, "question-driven-scene-plan.enriched.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(plan, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private static IReadOnlyList<RequestedOutputCompletion> BuildRequestedOutputCompletion(ProductionPhaseContext context, IReadOnlyList<ProductionPhaseResult> phaseResults)
    {
        var method = typeof(ProductionPipelineExecutionService)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(m => m.Name == "BuildRequestedOutputCompletion" && m.GetParameters().Length == 2);
        return (IReadOnlyList<RequestedOutputCompletion>)method.Invoke(null, [context, phaseResults])!;
    }

    private static bool InvokePhase18DiagnosticsValidator(string methodName, JsonNode? diagnostics)
    {
        var method = typeof(ProductionPipelineExecutionService).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (bool)method!.Invoke(null, [diagnostics])!;
    }

    private static string InvokePhase18MotionV2StrengthResolver(string? requestMotionV2Strength, string? planMotionV2Strength)
    {
        var method = typeof(ProductionPipelineExecutionService).GetMethod("ResolvePhase18MotionV2Strength", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (string)method!.Invoke(null, [requestMotionV2Strength, planMotionV2Strength])!;
    }

    private static bool InvokePhase18MotionV2StrengthMismatch(string? requestMotionV2Strength, string? motionV2StrengthUsed)
    {
        var method = typeof(ProductionPipelineExecutionService).GetMethod("HasMotionV2StrengthMismatch", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (bool)method!.Invoke(null, [requestMotionV2Strength, motionV2StrengthUsed])!;
    }

    private static bool InvokePhase18MotionV2StrengthOverrideWarning(string? requestMotionV2Strength, string? planMotionV2Strength)
    {
        var method = typeof(ProductionPipelineExecutionService).GetMethod("ShouldWarnMotionV2StrengthRequestOverride", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (bool)method!.Invoke(null, [requestMotionV2Strength, planMotionV2Strength])!;
    }

    private static void WritePhase10SceneAssets(string root, string profile, int count, int? skipFinalSceneNumber = null)
    {
        for (var i = 1; i <= count; i++)
        {
            var sceneId = $"scene-{i:000}";
            var sceneDirectory = Path.Combine(root, "scene-assets", profile, sceneId);
            Directory.CreateDirectory(sceneDirectory);
            if (skipFinalSceneNumber == i) continue;
            File.WriteAllBytes(Path.Combine(sceneDirectory, $"{sceneId}-final.png"), [1, 2, 3]);
        }
    }

    [Fact]
    public void OverwriteCleanup_Phase13Only_PreservesEarlierValidationAndOtherOutputRoots()
    {
        var baseContext = CreateContext("MeteorShower", ["Gallery"]);
        var deleted = new List<string>();
        var context = baseContext with
        {
            StartPhaseNo = 13,
            EndPhaseNo = 13,
            OverwriteExisting = true,
            DeletedFilesDueToOverwrite = deleted,
            PipelineRequest = baseContext.PipelineRequest with { StartPhaseNo = 13, EndPhaseNo = 13, OverwriteExisting = true }
        };

        Directory.CreateDirectory(context.ExecutionContext.ValidationRoot!);
        Directory.CreateDirectory(Path.Combine(context.OutputRoot, "gallery"));
        Directory.CreateDirectory(context.ExecutionContext.HeroRoot!);
        Directory.CreateDirectory(context.ExecutionContext.ThumbnailRoot!);
        Directory.CreateDirectory(context.ExecutionContext.QuestionRoot!);
        Directory.CreateDirectory(context.ExecutionContext.SceneRoot!);
        Directory.CreateDirectory(context.ExecutionContext.NarrationRoot!);
        Directory.CreateDirectory(context.ExecutionContext.TtsRoot!);
        Directory.CreateDirectory(context.ExecutionContext.VideoAssemblyRoot!);

        for (var phaseNo = 1; phaseNo <= 13; phaseNo++)
            File.WriteAllText(Path.Combine(context.ExecutionContext.ValidationRoot!, $"phase-{phaseNo:00}-validation.json"), "{}");
        File.WriteAllText(Path.Combine(context.OutputRoot, "gallery", "gallery-01.png"), "gallery");
        File.WriteAllText(Path.Combine(context.ExecutionContext.HeroRoot!, "hero-final.png"), "hero");
        File.WriteAllText(Path.Combine(context.ExecutionContext.ThumbnailRoot!, "thumbnail.png"), "thumbnail");
        File.WriteAllText(Path.Combine(context.ExecutionContext.QuestionRoot!, "question-answer-set.json"), "questions");
        File.WriteAllText(Path.Combine(context.ExecutionContext.SceneRoot!, "scene.png"), "scene");
        File.WriteAllText(Path.Combine(context.ExecutionContext.NarrationRoot!, "narration.txt"), "narration");
        File.WriteAllText(Path.Combine(context.ExecutionContext.TtsRoot!, "narration.mp3"), "tts");
        File.WriteAllText(Path.Combine(context.ExecutionContext.VideoAssemblyRoot!, "final-video-short.mp4"), "video");

        var method = typeof(ProductionPipelineExecutionService).GetMethod("ClearPhaseRangeOutputsForOverwrite", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        method!.Invoke(null, [context]);

        for (var phaseNo = 1; phaseNo <= 12; phaseNo++)
            Assert.True(File.Exists(Path.Combine(context.ExecutionContext.ValidationRoot!, $"phase-{phaseNo:00}-validation.json")), $"phase {phaseNo} validation should be preserved");

        Assert.False(File.Exists(Path.Combine(context.ExecutionContext.ValidationRoot!, "phase-13-validation.json")));
        Assert.False(Directory.Exists(Path.Combine(context.OutputRoot, "gallery")));
        Assert.True(File.Exists(Path.Combine(context.ExecutionContext.HeroRoot!, "hero-final.png")));
        Assert.True(File.Exists(Path.Combine(context.ExecutionContext.ThumbnailRoot!, "thumbnail.png")));
        Assert.True(File.Exists(Path.Combine(context.ExecutionContext.QuestionRoot!, "question-answer-set.json")));
        Assert.True(File.Exists(Path.Combine(context.ExecutionContext.SceneRoot!, "scene.png")));
        Assert.True(File.Exists(Path.Combine(context.ExecutionContext.NarrationRoot!, "narration.txt")));
        Assert.True(File.Exists(Path.Combine(context.ExecutionContext.TtsRoot!, "narration.mp3")));
        Assert.True(File.Exists(Path.Combine(context.ExecutionContext.VideoAssemblyRoot!, "final-video-short.mp4")));
        Assert.DoesNotContain(deleted, path => path.Contains("phase-12-validation.json", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(deleted, path => path.Contains("phase-13-validation.json", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public void PlanetConjunctionNarrationV22_HumanizesBestTimeAndSkyGuideFragments()
    {
        var naturalTime = typeof(ProductionPipelineExecutionService).GetMethod("NaturalViewingWindow", BindingFlags.NonPublic | BindingFlags.Static);
        var naturalDirection = typeof(ProductionPipelineExecutionService).GetMethod("NaturalSkyDirection", BindingFlags.NonPublic | BindingFlags.Static);

        var time = (string)naturalTime!.Invoke(null, ["Jun 9, 2026 7:23 PM"])!;
        var direction = (string)naturalDirection!.Invoke(null, ["the western sky after sunset horizon"])!;

        Assert.Equal("June ninth", time);
        Assert.Equal("the western horizon", direction);
        Assert.DoesNotContain("7:23", time);
        Assert.DoesNotContain("after sunset horizon", direction);
    }

    [Fact]
    public void PlanetConjunctionNarrationV22_DiagnosticsCatchCauseSkyGuideAndBestTimeQuality()
    {
        var causeMethod = typeof(ProductionPipelineExecutionService).GetMethod("DetectCauseDuplication", BindingFlags.NonPublic | BindingFlags.Static);
        var skyGuideMethod = typeof(ProductionPipelineExecutionService).GetMethod("SkyGuideGrammarPassed", BindingFlags.NonPublic | BindingFlags.Static);
        var bestTimeMethod = typeof(ProductionPipelineExecutionService).GetMethod("BestTimeHumanizationPassed", BindingFlags.NonPublic | BindingFlags.Static);

        var causeDuplicationDetected = (bool)causeMethod!.Invoke(null, [new[] { "Although the planets appear close together, they are separated by distance. Their apparent closeness is because they appear close from perspective. This repeats the alignment perspective again." }])!;
        var skyGuideGrammarPassed = (bool)skyGuideMethod!.Invoke(null, [new[] { "About thirty minutes after sunset, turn your attention toward the western horizon. There you'll find two bright planets appearing unusually close together above the skyline." }])!;
        var bestTimeHumanizationPassed = (bool)bestTimeMethod!.Invoke(null, [new[] { "The conjunction reaches its finest appearance during the evenings surrounding June ninth. Arriving a little before sunset gives your eyes time to adjust as the sky slowly darkens." }])!;

        Assert.True(causeDuplicationDetected);
        Assert.True(skyGuideGrammarPassed);
        Assert.True(bestTimeHumanizationPassed);
    }

    private static ProductionPhaseContext CreateContext(string eventType, IReadOnlyList<string> requestedOutputs, string? shortTitleOverride = null, bool enableSceneVariants = false)
    {
        var planId = Guid.NewGuid();
        var outputRoot = Path.Combine(Path.GetTempPath(), "astro-pulse-phase-gating-tests", planId.ToString("N"));
        var request = new ContentPlanProductionPipelineRequest(
            planId,
            "AstronomyEvent",
            $"Current {eventType} Event",
            shortTitleOverride ?? $"{eventType} Tonight",
            eventType,
            "us",
            "en",
            [eventType == "PlanetPairing" ? "Venus" : "Moon"],
            eventType == "PlanetPairing" ? ["Jupiter"] : [],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddHours(1),
            DateTimeOffset.UtcNow.AddHours(2),
            DateTimeOffset.UtcNow,
            null,
            string.Join("+", requestedOutputs),
            requestedOutputs,
            null,
            null,
            null,
            null,
            "Verified",
            "Test",
            "Current event strategy",
            "9 PM",
            "western sky",
            "United States",
            null,
            "9 PM to midnight",
            null,
            null,
            requestedOutputs,
            [],
            []);
        var intelligence = new ProductionEventIntelligence(
            "Astronomy",
            eventType,
            request.Title,
            request.ShortTitle,
            request.StartUtc,
            request.PeakUtc,
            request.LocalPeakTime,
            request.BestViewingWindowLocal,
            request.SkyDirectionHint,
            request.VisibilityRegion,
            request.PrimaryObjects,
            request.SecondaryObjects,
            null,
            request.MoonInterference,
            request.MoonIlluminationPercent,
            null,
            [],
            [],
            [],
            [],
            []);
        var executionContext = new ProductionPipelineExecutionContext(
            true,
            planId,
            Guid.NewGuid(),
            null,
            true,
            true,
            "Approved",
            "Approved",
            true,
            true,
            "Verified",
            request.ContentStrategy,
            request.RegionId,
            request.Language,
            request.RequestedOutputs,
            request.Category,
            request.PlannedFormat,
            DateTimeOffset.UtcNow.Year,
            request.EventType,
            Path.Combine(outputRoot, "plan-input"),
            Path.Combine(outputRoot, "question-engine"),
            Path.Combine(outputRoot, "scene-approval-v3"),
            Path.Combine(outputRoot, "hero"),
            Path.Combine(outputRoot, "thumbnails"),
            Path.Combine(outputRoot, "narration"),
            Path.Combine(outputRoot, "tts"),
            Path.Combine(outputRoot, "video-assembly"),
            Path.Combine(outputRoot, "validation"),
            intelligence,
            new GenericAstronomyEventStrategy(),
            null);
        var pipelineRequest = new ProductionPipelineRequest(request, Guid.NewGuid(), outputRoot, false, ExecutionContext: executionContext, EnableSceneVariants: enableSceneVariants);
        return new ProductionPhaseContext(pipelineRequest, request, Guid.NewGuid(), Guid.NewGuid().ToString("D"), outputRoot, executionContext, intelligence, new GenericAstronomyEventStrategy(), false, false, 1, 20, false);
    }
}
