#!/bin/bash
# Prepara el VPS para servir los instaladores en https://iris.atmosferast.com/downloads/windows/ (docs/plans/12 §E).
# Idempotente. Se corre UNA vez, como root en el VPS:   ssh atmosfera 'bash -s' < Scripts/vps-setup-downloads.sh
#
# Qué hace:
#   1. Crea /var/www/iris-downloads/windows y se la da al usuario de despliegue (el de SSH_USER en GitHub).
#   2. Agrega un `location` al sitio iris.atmosferast.com que sirve esa carpeta como archivos estáticos.
#      La ruta /downloads (la página) y todo lo demás siguen yendo a la web (Next.js).
#   3. Prueba la configuración (nginx -t); si falla, restaura la copia y no recarga nada.
set -euo pipefail

SITE=/etc/nginx/sites-enabled/iris.atmosferast.com
DIR=/var/www/iris-downloads/windows
DEPLOY_USER="${DEPLOY_USER:-deploy}"

mkdir -p "$DIR"
chown -R "$DEPLOY_USER:$DEPLOY_USER" /var/www/iris-downloads
chmod -R 755 /var/www/iris-downloads

if grep -q "/downloads/windows/" "$SITE"; then
  echo "El sitio ya sirve /downloads/windows/: nada que cambiar."
  exit 0
fi

BACKUP=/root/iris.atmosferast.com.bak-downloads
cp "$SITE" "$BACKUP"

python3 - "$SITE" <<'PY'
import sys
path = sys.argv[1]
text = open(path).read()
block = '''
\t# Instaladores de Windows: archivos estáticos (atm-iris-win, docs/plans/12). La página /downloads es de la web.
\tlocation /downloads/windows/ {
\t\talias /var/www/iris-downloads/windows/;
\t\tautoindex off;
\t\tadd_header Cache-Control "no-cache";
\t}
\t# El feed jamás se guarda en caché: de él depende que la app vea la versión nueva.
\tlocation = /downloads/windows/releases.win.json {
\t\talias /var/www/iris-downloads/windows/releases.win.json;
\t\tadd_header Cache-Control "no-store";
\t}
'''
marker = "\tlocation / {"
assert text.count(marker) == 1, "No encuentro un único `location /` en el sitio."
open(path, "w").write(text.replace(marker, block.lstrip("\n") + "\n" + marker, 1))
PY

if nginx -t; then
  systemctl reload nginx
  echo "Listo: /downloads/windows/ servido desde $DIR."
else
  echo "nginx -t falló: restauro la copia y no recargo." >&2
  mv "$BACKUP" "$SITE"
  exit 1
fi
