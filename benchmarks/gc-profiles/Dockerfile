FROM node:24-bookworm-slim AS node

FROM mcr.microsoft.com/dotnet/runtime:10.0

RUN apt-get update \
    && apt-get install -y --no-install-recommends time \
    && rm -rf /var/lib/apt/lists/*

COPY --from=node /usr/local/bin/node /usr/local/bin/node

RUN dotnet --info >/dev/null && node --version

CMD ["tail", "-f", "/dev/null"]

