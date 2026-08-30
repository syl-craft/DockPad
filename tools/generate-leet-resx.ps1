<#
.SYNOPSIS
    Genere DockPad.Core/Resources/Strings.qps-Ploc.resx a partir du francais, en leet speak.

.DESCRIPTION
    La langue « 1337 » n'est pas traduite a la main : elle est DERIVEE du francais par
    substitution de glyphes. C'est ce qui la rend gratuite a maintenir — une cle ajoutee au
    francais se retrouve ici en relancant ce script, et jamais oubliee.

    Deux regles, et elles ne sont pas negociables :

      1. Rien de ce qui est entre accolades n'est touche. Un placeholder ({0}) ou un gabarit
         SmartFormat ({0:plural:regle|regles}) transforme deviendrait illisible pour le
         formateur, et les gardes de traduction refusent justement ce cas.
      2. La longueur du texte ne change pas. La pseudo-localisation classique allonge les
         chaines pour reveler les troncatures ; on ne le fait pas ici, parce que le but de
         cette langue est d'etre utilisable pour le plaisir. Elle revele quand meme les
         chaines restees EN DUR dans le code : elles apparaissent en clair au milieu du leet.

.EXAMPLE
    pwsh tools/generate-leet-resx.ps1
#>
[CmdletBinding()]
param(
    [string]$Source = (Join-Path $PSScriptRoot "..\DockPad.Core\Resources\Strings.fr.resx"),
    [string]$Target = (Join-Path $PSScriptRoot "..\DockPad.Core\Resources\Strings.qps-Ploc.resx")
)

$ErrorActionPreference = "Stop"

# Substitution volontairement legere : le texte doit rester lisible. Les voyelles accentuees
# suivent leur lettre de base, sinon « predefinis » et « prédéfinis » ne se ressembleraient plus.
# Les cles sont en minuscules et la recherche aussi : une table de hachage PowerShell est
# insensible a la casse, « a » et « A » y entreraient en collision. Sans consequence ici,
# les deux donnant le meme chiffre.
$map = @{
    'a' = '4'; 'à' = '4'; 'â' = '4'
    'e' = '3'; 'é' = '3'; 'è' = '3'; 'ê' = '3'; 'ë' = '3'
    'i' = '1'; 'î' = '1'; 'ï' = '1'
    'o' = '0'; 'ô' = '0'; 'ö' = '0'
    's' = '5'
}

function ConvertTo-Leet([string]$text) {
    $out = [System.Text.StringBuilder]::new($text.Length)
    $depth = 0
    foreach ($ch in $text.ToCharArray()) {
        if     ($ch -eq '{') { $depth++ }
        elseif ($ch -eq '}') { $depth = [Math]::Max(0, $depth - 1) }

        # Hors accolades seulement : dedans vivent les placeholders et les gabarits de pluriel.
        $lower = [string][char]::ToLowerInvariant($ch)
        if ($depth -eq 0 -and $ch -ne '}' -and $map.ContainsKey($lower)) {
            [void]$out.Append($map[$lower])
        } else {
            [void]$out.Append($ch)
        }
    }
    return $out.ToString()
}

$xml = New-Object System.Xml.XmlDocument
$xml.PreserveWhitespace = $true
$xml.Load((Resolve-Path $Source))

$count = 0
foreach ($data in $xml.SelectNodes("//data")) {
    $value = $data.SelectSingleNode("value")
    if ($null -eq $value) { continue }
    $value.InnerText = ConvertTo-Leet $value.InnerText
    $count++
}

$settings = New-Object System.Xml.XmlWriterSettings
$settings.Indent = $false
$settings.Encoding = New-Object System.Text.UTF8Encoding($false)
$writer = [System.Xml.XmlWriter]::Create($Target, $settings)
$xml.Save($writer)
$writer.Dispose()

Write-Output "$count cles ecrites dans $Target"
