FROM python:3.12-slim

WORKDIR /opt/asn
COPY requirements.txt /tmp/asn-requirements.txt
RUN pip install --no-cache-dir --require-hashes --only-binary=:all: -r /tmp/asn-requirements.txt && rm /tmp/asn-requirements.txt
COPY app.py admin.py package_source.py Dockerfile docker-compose.yml requirements.txt .env.example .dockerignore README.md VERSION /tmp/asn-source/
COPY tests/ /tmp/asn-source/tests/
COPY web/ /tmp/asn-source/web/
COPY licenses/ /tmp/asn-source/licenses/
COPY licenses/ /opt/asn/licenses/
COPY web/ /opt/asn/web/
RUN python /tmp/asn-source/package_source.py --output /opt/asn/source.zip

RUN useradd --system --create-home appuser
RUN mkdir -p /opt/asn /data \
    && install -m 0644 /tmp/asn-source/app.py /opt/asn/app.py \
    && install -m 0644 /tmp/asn-source/admin.py /opt/asn/admin.py \
    && install -m 0644 /tmp/asn-source/VERSION /opt/asn/VERSION \
    && chmod 0755 /opt /opt/asn /data \
    && find /opt/asn/web /opt/asn/licenses -type d -exec chmod 0755 {} + \
    && find /opt/asn/web /opt/asn/licenses -type f -exec chmod 0644 {} + \
    && chmod 0644 /opt/asn/source.zip \
    && chown appuser:appuser /data \
    && rm -r /tmp/asn-source
ENV PYTHONUNBUFFERED=1 PYTHONDONTWRITEBYTECODE=1
USER appuser
RUN python -c "from pathlib import Path; [compile(p.read_text(),str(p),'exec') for p in Path('/opt/asn').glob('*.py')]"

EXPOSE 8765
CMD ["python", "/opt/asn/app.py"]
