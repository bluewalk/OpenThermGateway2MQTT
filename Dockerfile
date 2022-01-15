# STAGE01 - Build application and its dependencies
FROM mcr.microsoft.com/dotnet/sdk:6.0-alpine AS build
WORKDIR /app
COPY . ./
RUN dotnet restore

# STAGE02 - Publish the application
FROM build AS publish
WORKDIR /app/Net.Bluewalk.OpenThermGateway2Mqtt
RUN dotnet publish -c Release -o ../out --self-contained --runtime linux-musl-x64
RUN rm ../out/*.pdb

# STAGE03 - Create the final image
FROM alpine:latest
LABEL Description="OpenThermGateway2MQTT image"
LABEL Maintainer="Bluewalk"

RUN apk add --no-cache icu-libs tzdata

WORKDIR /app
COPY --from=publish /app/out ./

CMD "/app/Net.Bluewalk.OpenThermGateway2Mqtt"
