using System.IO;
using DockPad.Models;
using DockPad.Services;

namespace DockPad.Tests;

/// <summary>
/// Prédéfini « agent local (Ollama) » : <c>ollama launch &lt;harness&gt; --model &lt;modèle&gt;</c>.
/// </summary>
/// <remarks>
/// Les deux réglages finissent dans le registre puis dans la ligne de commande d'un terminal —
/// PowerShell <c>-Command</c> pour certains. Ils sont donc validés par liste blanche, jamais
/// échappés : chaque caractère qui sépare, cite ou développe une commande est refusé.
/// </remarks>
public class OllamaAgentTests : IDisposable
{
    private const string OllamaExe = @"C:\Users\moi\AppData\Local\Programs\Ollama\ollama.exe";

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usage_{Guid.NewGuid():N}.json");
    public void Dispose() { if (File.Exists(_path)) File.Delete(_path); }

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    [InlineData("copilot-cli")]
    [InlineData("hermes-desktop")]
    public void Harness_Connu_EstAccepte(string harness)
    {
        Assert.True(OllamaAgent.IsValidHarness(harness));
    }

    [Theory]
    [InlineData("ministral-3:3b")]
    [InlineData("qwen3-coder:30b")]
    [InlineData("hf.co/bartowski/Llama-3.2-1B-Instruct-GGUF:Q4_K_M")]
    [InlineData("gpt-oss:20b-cloud")]
    public void Modele_Connu_EstAccepte(string model)
    {
        Assert.True(OllamaAgent.IsValidModel(model));
    }

    public static TheoryData<string> Injections => new()
    {
        " ", ";", "&", "|", "\"", "'", "`", "$", "(", ")", "<", ">", "%", "\n", "\r", "\t",
        "{", "}", "^", "!", "*", "?", "\\", ",", "#", "@",
    };

    [Theory]
    [MemberData(nameof(Injections))]
    public void Harness_AvecCaractereDangereux_EstRefuse(string character)
    {
        Assert.False(OllamaAgent.IsValidHarness($"claude{character}calc"));
    }

    [Theory]
    [MemberData(nameof(Injections))]
    public void Modele_AvecCaractereDangereux_EstRefuse(string character)
    {
        Assert.False(OllamaAgent.IsValidModel($"ministral-3:3b{character}calc"));
    }

    /// <summary>En .NET, <c>$</c> accepte un saut de ligne final : l'ancre doit être <c>\z</c>.</summary>
    [Fact]
    public void SautDeLigneFinal_EstRefuse()
    {
        Assert.False(OllamaAgent.IsValidHarness("claude\n"));
        Assert.False(OllamaAgent.IsValidModel("ministral-3:3b\n"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("-claude")]
    [InlineData("--help")]
    [InlineData("Claude")]
    public void Harness_VideOuOption_EstRefuse(string harness)
    {
        Assert.False(OllamaAgent.IsValidHarness(harness));
    }

    [Theory]
    [InlineData("")]
    [InlineData("--restore")]
    [InlineData(":3b")]
    public void Modele_VideOuOption_EstRefuse(string model)
    {
        Assert.False(OllamaAgent.IsValidModel(model));
    }

    [Fact]
    public void Commande_LanceLeHarnessSurLeModele()
    {
        var command = OllamaAgent.RunCommand(OllamaExe, harness: "claude", model: "ministral-3:3b");

        Assert.Equal($"\"{OllamaExe}\" launch claude --model ministral-3:3b", command);
    }

    /// <summary>
    /// Le lanceur recompose les arguments découpés par Windows : un harness et un modèle valides
    /// doivent en ressortir tels quels, sans guillemets que le terminal relirait autrement.
    /// </summary>
    [Fact]
    public void Commande_TraverseLeLanceurSansEtreModifiee()
    {
        var command = TerminalRequest.JoinArguments(["launch", "claude", "--model", "hf.co/org/model:Q4_K_M"]);

        Assert.Equal("launch claude --model hf.co/org/model:Q4_K_M", command);
    }

    [Theory]
    [InlineData(" claude")]
    [InlineData("claude ")]
    public void Harness_AvecEspacePeripherique_EstRefuse(string harness)
    {
        Assert.False(OllamaAgent.IsValidHarness(harness));
    }

    [Fact]
    public void Commande_ReglageInvalide_EstAbsente()
    {
        Assert.Null(OllamaAgent.RunCommand(OllamaExe, harness: "claude", model: "x; calc"));
        Assert.Null(OllamaAgent.RunCommand(OllamaExe, harness: "", model: "ministral-3:3b"));
        Assert.Null(OllamaAgent.RunCommand(OllamaExe, harness: "claude", model: ""));
    }

    [Fact]
    public void Predefini_Configure_OuvreLeDossierDuClicDroitDansLeTerminal()
    {
        var preset = PresetService.BuildOllamaAgent(OllamaExe, harness: "claude", model: "ministral-3:3b");

        Assert.NotNull(preset);
        Assert.Equal("OpenOllamaAgent", preset.RegistryKey);
        Assert.Equal(ContextMenuTarget.FolderBackground, preset.Target);
        Assert.Contains("--terminal \"%V\\.\" -- ", preset.Command);
        Assert.EndsWith($"-- \"{OllamaExe}\" launch claude --model ministral-3:3b", preset.Command);
        Assert.Equal(OllamaExe, preset.IconPath);
    }

    [Fact]
    public void Predefini_OllamaIntrouvable_EstAbsent()
    {
        Assert.Null(PresetService.BuildOllamaAgent(ollamaExe: null, harness: "claude", model: "ministral-3:3b"));
    }

    [Theory]
    [InlineData("", "ministral-3:3b")]
    [InlineData("claude", "")]
    [InlineData("", "")]
    [InlineData("claude", "ministral-3:3b & calc")]
    public void Predefini_ReglageIncompletOuInvalide_EstAbsent(string harness, string model)
    {
        Assert.Null(PresetService.BuildOllamaAgent(OllamaExe, harness, model));
    }

    [Fact]
    public void Predefini_ChangerDeModele_PasseAMettreAJour()
    {
        var before = PresetService.BuildOllamaAgent(OllamaExe, harness: "claude", model: "ministral-3:3b");
        var after = PresetService.BuildOllamaAgent(OllamaExe, harness: "claude", model: "qwen3-coder:30b");
        Assert.NotNull(before);
        Assert.NotNull(after);

        var status = PresetService.CompareStatus((before.DisplayName, before.Command, before.IconPath), after);

        Assert.Equal(PresetStatus.UpdateAvailable, status);
    }

    [Fact]
    public void Config_ParDefaut_ChampsVides()
    {
        var config = UsageConfigService.Load(_path);

        Assert.Equal("", config.OllamaHarness);
        Assert.Equal("", config.OllamaModel);
    }

    [Fact]
    public void Config_SavePuisLoad_ConserveLeReglage()
    {
        UsageConfigService.Save(new UsageConfig { OllamaHarness = "claude", OllamaModel = "ministral-3:3b" }, _path);

        var config = UsageConfigService.Load(_path);

        Assert.Equal("claude", config.OllamaHarness);
        Assert.Equal("ministral-3:3b", config.OllamaModel);
    }

    /// <summary>
    /// <c>usage.json</c> se modifie à la main : une valeur écrite hors de la fenêtre ne doit pas
    /// contourner la validation de la saisie.
    /// </summary>
    [Fact]
    public void Config_ValeurInvalideDansLeFichier_EstEcartee()
    {
        File.WriteAllText(_path, """{ "ollamaHarness": "claude & calc", "ollamaModel": "x\"; calc; \"" }""");

        var config = UsageConfigService.Load(_path);

        Assert.Equal("", config.OllamaHarness);
        Assert.Equal("", config.OllamaModel);
    }
}
