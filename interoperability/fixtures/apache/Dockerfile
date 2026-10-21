FROM httpd:2.4.65-bookworm@sha256:fbc12199ccad031d8047e9c789d65aceee2d14f99ba90664cd3a3996867a5582
RUN sed -i \
    -e 's/^#LoadModule dav_module/LoadModule dav_module/' \
    -e 's/^#LoadModule dav_fs_module/LoadModule dav_fs_module/' \
    -e 's/^#LoadModule auth_basic_module/LoadModule auth_basic_module/' \
    -e 's/^#LoadModule authn_file_module/LoadModule authn_file_module/' \
    conf/httpd.conf \
    && printf '\nInclude conf/interop.conf\n' >> conf/httpd.conf
COPY interop.conf conf/interop.conf
COPY start.sh /usr/local/bin/interop-start
RUN chmod +x /usr/local/bin/interop-start
CMD ["interop-start"]
