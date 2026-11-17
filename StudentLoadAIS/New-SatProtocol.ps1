[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $EvidenceJson,
    [Parameter(Mandatory)][string] $Destination
)

$ErrorActionPreference = 'Stop'
$record = Get-Content -Raw -LiteralPath $EvidenceJson -Encoding UTF8 | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($record.release) -or
    [string]::IsNullOrWhiteSpace($record.environment) -or
    [string]::IsNullOrWhiteSpace($record.executedAt) -or -not $record.cases) {
    throw 'Evidence must include release, environment, executedAt and cases.'
}
[void][DateTimeOffset]::Parse($record.executedAt, [Globalization.CultureInfo]::InvariantCulture)
if (@($record.cases | Group-Object id | Where-Object Count -gt 1).Count -gt 0) {
    throw 'Evidence contains duplicate case IDs.'
}

function ConvertTo-Cell([string] $Value) {
    return [System.Net.WebUtility]::HtmlEncode($Value).Replace('|', '\|').Replace("`r", '').Replace("`n", '<br>')
}

$lines = [Collections.Generic.List[string]]::new()
$lines.Add('# Протокол проверок SAT — АИС «Управление студенческой нагрузкой»')
$lines.Add('')
$lines.Add("- Версия: $(ConvertTo-Cell $record.release)")
$lines.Add("- Стенд: $(ConvertTo-Cell $record.environment)")
$lines.Add("- Время последней проверки: $(ConvertTo-Cell $record.executedAt)")
$lines.Add('')
$lines.Add('| ID | Проверка | Ожидаемый результат | Факт и свидетельство | Статус |')
$lines.Add('|---|---|---|---|---|')
$passed = 0
foreach ($case in $record.cases) {
    if ($case.status -notin @('PASS','FAIL','NOT RUN') -or
        [string]::IsNullOrWhiteSpace($case.id) -or
        [string]::IsNullOrWhiteSpace($case.title) -or
        [string]::IsNullOrWhiteSpace($case.expected) -or
        [string]::IsNullOrWhiteSpace($case.actual)) {
        throw "Case '$($case.id)' has an invalid status or missing data."
    }
    if ($case.status -eq 'PASS') { $passed++ }
    $lines.Add("| $(ConvertTo-Cell $case.id) | $(ConvertTo-Cell $case.title) | $(ConvertTo-Cell $case.expected) | $(ConvertTo-Cell $case.actual) | $($case.status) |")
}

$lines.Add('')
$lines.Add("Подтверждено результатов на указанном стенде: $passed из $(@($record.cases).Count).")
$lines.Add('')
$lines.Add('Этот протокол отражает испытания на указанном стенде. Решение о приёмке оформляет заказчик.')
$absolute = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Destination)
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($absolute))
[IO.File]::WriteAllLines($absolute, $lines, [Text.UTF8Encoding]::new($false))
Write-Output $absolute

