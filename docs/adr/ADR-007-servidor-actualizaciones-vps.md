# ADR-007: Servidor de actualizaciones en VPS propio

- **Estado**: Pendiente de implementar
- **Fecha**: 2026-03-16

---

## Contexto

Velopack requiere un servidor HTTP que sirva los archivos de release (ver ADR-006). Se necesita definir dónde y cómo se aloja ese servidor.

## Decisión

Usar el **VPS propio** con **Nginx en Docker** sirviendo una carpeta estática bajo el subdominio `releases.tworockets.com.mx/trvisionai/`.

## Infraestructura del VPS

| Dato | Valor |
|---|---|
| OS | Ubuntu 22.04 |
| Runtime | Docker + Docker Compose |
| Dominio | tworockets.com.mx |
| Subdominio | releases.tworockets.com.mx |
| SSL | Let's Encrypt (Certbot) |

## Arquitectura propuesta

```
releases.tworockets.com.mx
└── /trvisionai/
    ├── releases.json          ← índice de versiones (generado por vpk)
    ├── TRVisionAI-1.0.0-win-full.nupkg
    ├── TRVisionAI-1.0.1-win-delta.nupkg
    └── TRVisionAI-win-Setup.exe
```

## Plan de implementación

### 1. VPS — docker-compose.yml

```yaml
services:
  releases:
    image: nginx:alpine
    volumes:
      - ./releases:/usr/share/nginx/html:ro
      - ./nginx.conf:/etc/nginx/conf.d/default.conf:ro
    restart: unless-stopped

  caddy:   # o certbot — para SSL automático
    image: caddy:alpine
    ports:
      - "80:80"
      - "443:443"
    volumes:
      - ./Caddyfile:/etc/caddy/Caddyfile
      - caddy_data:/data
    restart: unless-stopped

volumes:
  caddy_data:
```

### 2. Caddyfile (SSL automático)

```
releases.tworockets.com.mx {
    root * /srv/releases
    file_server
}
```

### 3. Script de publicación (`publish.ps1`) — máquina de desarrollo

```powershell
$version  = "1.0.1"
$vpsUser  = "deploy"
$vpsHost  = "IP_DEL_VPS"
$vpsPath  = "/srv/releases/trvisionai"

# 1. Publicar
dotnet publish TRVisionAI.Desktop -c Release -r win-x64 --self-contained false -o ./publish

# 2. Empaquetar con Velopack
vpk pack `
  --packId TRVisionAI `
  --packVersion $version `
  --packDir ./publish `
  --icon TRVisionAI.Desktop/Assets/app.ico `
  --outputDir ./releases

# 3. Subir al VPS
rsync -avz ./releases/ "${vpsUser}@${vpsHost}:${vpsPath}/"

Write-Host "v$version publicada en releases.tworockets.com.mx/trvisionai"
```

## Workflow de publicación (una vez implementado)

```
1. Incrementar versión en TRVisionAI.Desktop.csproj
2. Ejecutar: ./publish.ps1
3. La app detecta la nueva versión al iniciar y pregunta al usuario
```

## Estado actual

- [x] Velopack integrado en la app (ADR-006)
- [x] URL configurada como placeholder en `App.xaml.cs`
- [ ] Subdominio DNS creado en tworockets.com.mx
- [ ] docker-compose.yml en el VPS
- [ ] SSL configurado
- [ ] Script `publish.ps1` creado
- [ ] Primera versión publicada

## Consecuencias

- **Positivo**: Control total sobre el servidor — sin dependencia de GitHub ni servicios externos.
- **Positivo**: Caddy maneja SSL automáticamente con Let's Encrypt.
- **Positivo**: Nginx sirve archivos estáticos — mínimo consumo de recursos en el VPS.
- **Pendiente**: Configurar acceso SSH con llave para el script de rsync.
