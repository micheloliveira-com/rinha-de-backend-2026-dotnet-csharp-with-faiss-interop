# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

ARG LAUNCHING_FROM_VS
ARG FINAL_BASE_IMAGE=${LAUNCHING_FROM_VS:+aotdebug}


# =========================================================
# BASE
# =========================================================

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base

USER $APP_UID

WORKDIR /app

EXPOSE 8080


# =========================================================
# BUILD
# =========================================================

FROM mcr.microsoft.com/dotnet/sdk:10.0-aot AS build

RUN apt-get update && apt-get install -y \
    clang \
    zlib1g-dev \
    libopenblas-dev \
    libgomp1 \
    libgfortran5 \
 && rm -rf /var/lib/apt/lists/*

ARG BUILD_CONFIGURATION=Release

WORKDIR /src

COPY ["backend/backend.csproj", "backend/"]

COPY . .

WORKDIR "/src/backend"

RUN dotnet build \
    "./backend.csproj" \
    -c $BUILD_CONFIGURATION \
    -o /app/build

RUN ONLY_REBUILD=1 dotnet run -c Release

# =========================================================
# PUBLISH
# =========================================================

FROM build AS publish

ARG BUILD_CONFIGURATION=Release

RUN dotnet publish \
    "./backend.csproj" \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    /p:UseAppHost=true


# =========================================================
# AOT DEBUG
# =========================================================

FROM base AS aotdebug

USER root

RUN apt-get update && apt-get install -y \
    gdb \
 && rm -rf /var/lib/apt/lists/*

USER app


# =========================================================
# FINAL
# =========================================================

FROM ${FINAL_BASE_IMAGE:-mcr.microsoft.com/dotnet/runtime-deps:10.0} AS final

RUN apt-get update && apt-get install -y \
    libopenblas0 \
    libgomp1 \
    libgfortran5 \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app

ENV OMP_NUM_THREADS=4 \
    MKL_NUM_THREADS=4 \
    OPENBLAS_NUM_THREADS=4

EXPOSE 8080

COPY --from=publish /app/publish .

ENTRYPOINT ["./backend"]