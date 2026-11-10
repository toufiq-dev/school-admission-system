#!/bin/sh
set -eu
API_BASE_URL="${API_BASE_URL:-http://localhost:8080}"
cat > /usr/share/nginx/html/config.js <<EOC
window.__APP_CONFIG__ = {
  API_BASE_URL: "${API_BASE_URL}"
};
EOC
exec "$@"
