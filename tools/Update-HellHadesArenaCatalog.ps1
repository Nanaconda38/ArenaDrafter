param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\src\ArenaDrafter\Data\hellhades-arena-catalog.json')
)

$ErrorActionPreference = 'Stop'
$exportUri = 'https://hellhades.com/wp-json/hh-api/v3/raid/export'
$ratingsRoot = 'https://hellhades.com/wp-json/hh-api/v3/raid/ratings/'
$rarities = @{ Rare = 3; Epic = 4; Legendary = 5; Mythical = 6 }
$allowedRoles = @('cleanser', 'crowdcontrol', 'damageabsorption', 'damagedealer', 'debuffer', 'healer', 'reviver', 'skillmanipulator', 'speedmanipulator')

Write-Host 'Downloading the authorized HellHades RAID export...'
$response = Invoke-WebRequest -UseBasicParsing -Uri $exportUri -Headers @{ Accept = 'application/json' }
if ($response.StatusCode -ne 200 -or $response.Headers.'Content-Type' -notmatch '^application/json') {
    throw 'HellHades did not return the expected JSON export.'
}
$payload = $response.Content | ConvertFrom-Json
if ($null -eq $payload.champions -or @($payload.champions).Count -lt 100) {
    throw 'The HellHades export does not contain a plausible champion catalog.'
}

$compiled = [System.Collections.Generic.List[object]]::new()
foreach ($champion in @($payload.champions)) {
    if (-not $rarities.ContainsKey([string]$champion.rarity)) { continue }
    $heroId = [int]$champion.heroId
    if ($heroId -le 0) { continue } # Unreleased placeholders have no RAID identity yet.
    $baseId = $heroId - ($heroId % 10)
    $postId = [int]$champion.id
    $name = [string]$champion.champion
    $roles = @($champion.arena_roles | ForEach-Object { ([string]$_).Trim() } | Where-Object { $_ })
    if ($baseId -le 0 -or $baseId % 10 -ne 0 -or $postId -le 0 -or [string]::IsNullOrWhiteSpace($name) -or
        @($roles | Where-Object { $_ -notin $allowedRoles }).Count -ne 0) {
        throw "Invalid HellHades champion identity or roles for post $postId."
    }

    $forms = [System.Collections.Generic.List[object]]::new()
    if ($champion.rarity -eq 'Mythical') {
        $ratings = Invoke-RestMethod -Uri ($ratingsRoot + $postId) -Headers @{ Accept = 'application/json' }
        foreach ($rating in $ratings) {
            $formRoles = @(([string]$rating.arena_role).Split(',', [System.StringSplitOptions]::RemoveEmptyEntries) | ForEach-Object { $_.Trim() })
            if ($formRoles.Count -eq 0 -or @($formRoles | Where-Object { $_ -notin $allowedRoles }).Count -ne 0) {
                throw "Invalid HellHades Mythical form roles for post $postId."
            }
            $forms.Add([ordered]@{ form = [int]$rating.form; arenaRating = [int]$rating.arena_rating; arenaRoles = $formRoles })
        }
        Start-Sleep -Milliseconds 100
    }
    else {
        $rating = 0
        if ($null -ne $champion.forms.'1'.arena_rating) { $rating = [int]$champion.forms.'1'.arena_rating }
        $forms.Add([ordered]@{ form = 1; arenaRating = $rating; arenaRoles = $roles })
    }
    if ($forms.Count -eq 0 -or @($forms.form | Sort-Object -Unique).Count -ne $forms.Count) {
        throw "HellHades returned invalid form data for post $postId."
    }

    $compiled.Add([ordered]@{
        baseId = $baseId
        hellHadesPostId = $postId
        englishName = $name.Trim()
        rarity = $rarities[[string]$champion.rarity]
        arenaRoles = $roles
        forms = @($forms | Sort-Object form)
        sourceUrl = [string]$champion.url
        sourceUpdated = [string]$champion.last_updated
    })
}

$duplicates = @($compiled | Group-Object { $_['baseId'] } | Where-Object Count -ne 1)
if ($compiled.Count -lt 500 -or $duplicates.Count -ne 0) {
    throw "The compiled HellHades catalog has $($compiled.Count) records and $($duplicates.Count) duplicate RAID Base IDs."
}

$document = [ordered]@{
    version = 1
    generatedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    source = $exportUri
    champions = @($compiled | Sort-Object { [int]$_['baseId'] })
}
$directory = Split-Path -Parent $OutputPath
[System.IO.Directory]::CreateDirectory($directory) | Out-Null
$temporary = $OutputPath + '.tmp'
[System.IO.File]::WriteAllText($temporary, ($document | ConvertTo-Json -Depth 8), [System.Text.UTF8Encoding]::new($false))
Move-Item -LiteralPath $temporary -Destination $OutputPath -Force
Write-Host "Compiled $($compiled.Count) Rare, Epic, Legendary, and Mythical champions to $OutputPath"
