#!/bin/sh
set -e

: "${DES_API_URL:?DES_API_URL is required (e.g. https://api.example.com)}"
: "${DES_DEFAULT_TENANT:=root}"

export DES_API_URL DES_DEFAULT_TENANT

envsubst < /usr/share/nginx/html/config.json.template \
       > /usr/share/nginx/html/config.json
rm /usr/share/nginx/html/config.json.template

exec nginx -g 'daemon off;'
