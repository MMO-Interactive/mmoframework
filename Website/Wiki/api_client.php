<?php

declare(strict_types=1);

function api_base_url(): string
{
    $env = getenv('MMO_DASHBOARD_BASE_URL');
    if ($env === false || trim($env) === '') {
        return 'http://127.0.0.1:8081';
    }

    return rtrim(trim($env), '/');
}

function fetch_api_json(string $path, int $timeoutSeconds = 8): array
{
    $url = api_base_url() . $path;
    $ch = curl_init($url);
    curl_setopt_array($ch, [
        CURLOPT_RETURNTRANSFER => true,
        CURLOPT_CONNECTTIMEOUT => $timeoutSeconds,
        CURLOPT_TIMEOUT => $timeoutSeconds,
        CURLOPT_FOLLOWLOCATION => true,
        CURLOPT_HTTPHEADER => [
            'Accept: application/json'
        ]
    ]);

    $body = curl_exec($ch);
    $error = curl_error($ch);
    $httpCode = (int)curl_getinfo($ch, CURLINFO_HTTP_CODE);
    curl_close($ch);

    if ($body === false) {
        return [
            'ok' => false,
            'url' => $url,
            'status' => 0,
            'error' => $error !== '' ? $error : 'Unknown cURL error.',
            'data' => null,
        ];
    }

    $decoded = json_decode($body, true);
    if (json_last_error() !== JSON_ERROR_NONE) {
        return [
            'ok' => false,
            'url' => $url,
            'status' => $httpCode,
            'error' => 'Invalid JSON response: ' . json_last_error_msg(),
            'data' => null,
        ];
    }

    if ($httpCode >= 400) {
        $message = is_array($decoded) && isset($decoded['error']) ? (string)$decoded['error'] : 'HTTP ' . $httpCode;
        return [
            'ok' => false,
            'url' => $url,
            'status' => $httpCode,
            'error' => $message,
            'data' => $decoded,
        ];
    }

    return [
        'ok' => true,
        'url' => $url,
        'status' => $httpCode,
        'error' => null,
        'data' => $decoded,
    ];
}

function safe_int(mixed $value): int
{
    return is_numeric($value) ? (int)$value : 0;
}

function safe_text(mixed $value): string
{
    if ($value === null) {
        return '';
    }

    if (is_scalar($value)) {
        return (string)$value;
    }

    return '';
}
