FROM mcr.microsoft.com/dotnet/sdk:6.0
WORKDIR /app

# Copy csproj and restore as distinct layers
COPY *.csproj ./
RUN dotnet restore

# Copy everything else and build
COPY . .
RUN dotnet publish -c Release -o out

ENTRYPOINT ["./out/DotnetSlowMarketWatcher"]
