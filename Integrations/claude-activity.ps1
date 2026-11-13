# Read-only observer: never prints a permission decision or executes tool input.
$ErrorActionPreference = 'Stop'
try {
    $eventData = [Console]::In.ReadToEnd() | ConvertFrom-Json
    $session = [string]$eventData.session_id
    if ([string]::IsNullOrWhiteSpace($session)) { exit 0 }
    $activityDirectory = Join-Path $env:LOCALAPPDATA 'NovaIsland/Activity'
    [IO.Directory]::CreateDirectory($activityDirectory) | Out-Null
    $hasher = [Security.Cryptography.SHA256]::Create()
    $digest = $hasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($session))
    $hasher.Dispose()
    $key = ([BitConverter]::ToString($digest)).Replace('-', '').Substring(0, 16)
    $activityPath = Join-Path $activityDirectory ('claude-' + $key + '.json')
    $activity = @{app='Claude'; session=$session; task=''; model=''; tool=''; permission='Not reported'; workspace=[string]$eventData.cwd; status='idle'}
    if ([IO.File]::Exists($activityPath)) {
        $previous = [IO.File]::ReadAllText($activityPath) | ConvertFrom-Json
        foreach ($field in @('task','model','permission','status','tool')) { $activity[$field] = [string]$previous.$field }
    }
    if ($eventData.permission_mode) { $activity.permission = [string]$eventData.permission_mode }
    if ($eventData.model) { $activity.model = [string]$eventData.model }
    switch ([string]$eventData.hook_event_name) {
        'UserPromptSubmit' {
            $summary = ([string]$eventData.prompt -replace '\s+', ' ').Trim()
            $activity.task = $summary.Substring(0, [Math]::Min(180, $summary.Length))
            $activity.status = 'working'; $activity.tool = ''
        }
        'PreToolUse' { $activity.status = 'working'; $activity.tool = [string]$eventData.tool_name }
        'PermissionRequest' { $activity.status = 'waiting'; $activity.tool = [string]$eventData.tool_name }
        'PostToolUse' { $activity.status = 'working'; $activity.tool = [string]$eventData.tool_name }
        'PostToolUseFailure' { $activity.status = 'failed' }
        'Notification' { if ($eventData.notification_type -eq 'permission_prompt') { $activity.status = 'waiting' } }
        'Stop' { $activity.status = 'completed'; $activity.tool = '' }
        'SessionEnd' { $activity.status = 'idle'; $activity.tool = '' }
    }
    $activity.updated = [DateTimeOffset]::UtcNow.ToString('o')
    $temporaryPath = $activityPath + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    [IO.File]::WriteAllText($temporaryPath, ($activity | ConvertTo-Json -Compress), [Text.UTF8Encoding]::new($false))
    if ([IO.File]::Exists($activityPath)) { [IO.File]::Replace($temporaryPath, $activityPath, $null) }
    else { [IO.File]::Move($temporaryPath, $activityPath) }
} catch { }
exit 0
