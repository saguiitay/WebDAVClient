#!/bin/sh
set -eu
test -n "$DAV_USER"
test -n "$DAV_PASSWORD"
printf '%s\n' "$DAV_PASSWORD" | htpasswd -ci /usr/local/apache2/conf/interop.passwd "$DAV_USER"
mkdir -p /usr/local/apache2/htdocs/dav /usr/local/apache2/var
chown -R daemon:daemon /usr/local/apache2/htdocs/dav /usr/local/apache2/var
exec httpd-foreground
