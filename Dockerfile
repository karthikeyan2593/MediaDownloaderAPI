# Build Stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .

# Automatically find the .csproj file anywhere and build it
RUN dotnet publish $(find . -name "*.csproj" | head -n 1) -c Release -o /app

# Runtime Stage (with FFmpeg & Python for yt-dlp)
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .

# Install FFmpeg and Python
RUN apt-get update && apt-get install -y python3 ffmpeg curl

# Install yt-dlp directly
RUN curl -L https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp -o /usr/local/bin/yt-dlp
RUN chmod a+rx /usr/local/bin/yt-dlp

# Set Port
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

# Run the API
ENTRYPOINT ["dotnet", "MediaDownloaderAPI.dll"]
