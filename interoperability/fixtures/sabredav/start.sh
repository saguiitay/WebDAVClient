#!/bin/sh
set -eu
mkdir -p /var/www/data/files
chown -R www-data:www-data /var/www/data
exec apache2-foreground
