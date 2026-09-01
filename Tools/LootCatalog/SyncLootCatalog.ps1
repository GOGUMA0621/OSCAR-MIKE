param(
    [Parameter(Mandatory = $true)][string]$Workbook,
    [Parameter(Mandatory = $true)][string]$Output
)

$ErrorActionPreference = 'Stop'
$tsvHeaders = @('sequence', 'assetName', 'displayName', 'contentPack', 'valueMin', 'valueMax', 'category', 'basePrice', 'priceVariance', 'spawnEnabled', 'allowedZones', 'notes')

function Read-ZipXml([IO.Compression.ZipArchive]$archive, [string]$path) {
    $entry = $archive.GetEntry($path)
    if ($null -eq $entry) { throw "Missing XLSX entry: $path" }
    $stream = $entry.Open()
    try {
        $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::UTF8)
        try { return [xml]$reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    finally { $stream.Dispose() }
}

function Get-ColumnNumber([string]$reference) {
    $letters = ([regex]::Match($reference, '^[A-Z]+')).Value
    $number = 0
    foreach ($character in $letters.ToCharArray()) { $number = ($number * 26) + ([int]$character - [int][char]'A' + 1) }
    return $number
}

function Sanitize-Field([string]$text) {
    if ($null -eq $text) { return '' }
    $trimmed = $text.Trim()
    if ($trimmed.Contains("`t") -or $trimmed.Contains("`r") -or $trimmed.Contains("`n")) { throw 'Cell values cannot contain tabs or line breaks.' }
    return $trimmed
}

if (-not (Test-Path -LiteralPath $Workbook)) { throw "Excel catalog not found: $Workbook" }
Add-Type -AssemblyName System.IO.Compression
$fileStream = [IO.File]::Open((Resolve-Path -LiteralPath $Workbook).Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
$archive = [IO.Compression.ZipArchive]::new($fileStream, [IO.Compression.ZipArchiveMode]::Read)
try {
    $workbookXml = Read-ZipXml $archive 'xl/workbook.xml'
    $relationshipsXml = Read-ZipXml $archive 'xl/_rels/workbook.xml.rels'
    $sheetNode = $workbookXml.workbook.sheets.sheet | Where-Object { $_.name -eq 'LootCatalog' } | Select-Object -First 1
    if ($null -eq $sheetNode) { throw 'The XLSX file does not contain a LootCatalog sheet.' }
    $relationshipId = $sheetNode.GetAttribute('id', 'http://schemas.openxmlformats.org/officeDocument/2006/relationships')
    $relationship = $relationshipsXml.Relationships.Relationship | Where-Object { $_.Id -eq $relationshipId } | Select-Object -First 1
    if ($null -eq $relationship) { throw 'The LootCatalog worksheet relationship is missing.' }
    $sheetTarget = ([string]$relationship.Target).TrimStart('/')
    $sheetPath = if ($sheetTarget.StartsWith('xl/')) { $sheetTarget } else { 'xl/' + $sheetTarget }
    $sheetXml = Read-ZipXml $archive $sheetPath

    $sharedStrings = @()
    if ($null -ne $archive.GetEntry('xl/sharedStrings.xml')) {
        $sharedXml = Read-ZipXml $archive 'xl/sharedStrings.xml'
        $sharedStrings = @($sharedXml.sst.si | ForEach-Object { $_.InnerText })
    }

    $lines = [Collections.Generic.List[string]]::new()
    $lines.Add(($tsvHeaders -join "`t"))
    foreach ($row in @($sheetXml.worksheet.sheetData.row | Where-Object { [int]$_.r -gt 1 })) {
        $fields = [string[]]::new(12)
        foreach ($cell in @($row.c)) {
            $column = Get-ColumnNumber ([string]$cell.r)
            if ($column -lt 1 -or $column -gt 12) { continue }
            $value = ''
            if ($cell.t -eq 's') { $value = $sharedStrings[[int]$cell.v] }
            elseif ($cell.t -eq 'inlineStr') { $value = $cell.is.InnerText }
            elseif ($cell.t -eq 'b') { $value = if ([string]$cell.v -eq '1') { 'true' } else { 'false' } }
            else { $value = [string]$cell.v }
            $fields[$column - 1] = Sanitize-Field $value
        }
        if ([string]::IsNullOrWhiteSpace($fields[0])) { continue }
        if ($fields[9] -match '^(?i:true|1)$') { $fields[9] = 'true' }
        elseif ($fields[9] -match '^(?i:false|0)$') { $fields[9] = 'false' }
        else { throw "Spawn enabled must be TRUE or FALSE at Excel row $($row.r)." }
        $lines.Add(($fields -join "`t"))
    }

    $outputDirectory = Split-Path -Parent $Output
    if (-not (Test-Path -LiteralPath $outputDirectory)) { New-Item -ItemType Directory -Path $outputDirectory | Out-Null }
    [IO.File]::WriteAllLines($Output, $lines, [Text.UTF8Encoding]::new($false))
    Write-Output "Excel catalog sync complete: $($lines.Count - 1) rows"
}
finally {
    $archive.Dispose()
    $fileStream.Dispose()
}
