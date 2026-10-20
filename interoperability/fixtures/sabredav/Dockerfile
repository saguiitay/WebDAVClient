FROM composer:2.8.12@sha256:5248900ab8b5f7f880c2d62180e40960cd87f60149ec9a1abfd62ac72a02577c AS composer
FROM php:8.3.25-apache@sha256:963810f068ac26f61e62540f47392aa607a1419895a1c61084c1ffde208e9117
COPY --from=composer /usr/bin/composer /usr/local/bin/composer
RUN apt-get update && apt-get install -y --no-install-recommends libonig-dev unzip \
    && docker-php-ext-install mbstring \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /var/www
COPY composer.json ./
RUN composer install --no-dev --no-interaction --prefer-dist
RUN printf '<Directory /var/www/html>\nFallbackResource /index.php\n</Directory>\n' > /etc/apache2/conf-available/interop.conf \
    && a2enconf interop
COPY index.php /var/www/html/index.php
COPY start.sh /usr/local/bin/interop-start
RUN chmod +x /usr/local/bin/interop-start
CMD ["interop-start"]
