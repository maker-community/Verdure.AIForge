
# 此阶段用于生成服务项目
FROM --platform=$TARGETPLATFORM mcr.microsoft.com/dotnet/sdk:9.0 AS build
RUN dotnet workload install aspire

WORKDIR /app
COPY ["Directory.Packages.props", "."]
COPY ["Directory.Build.props", "."]
COPY ["NuGet.config", "."]
COPY ["Verdure.AIForge.sln", "."]
COPY ["src/.", "src/"]

COPY . .

RUN dotnet restore

RUN dotnet publish src/Verdure.AIForge.ApiService/Verdure.AIForge.ApiService.csproj -c Release -v d -o out

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/out .
EXPOSE 5500
ENV ASPNETCORE_URLS=http://+:5500
ENTRYPOINT ["dotnet", "Verdure.AIForge.ApiService.dll", "--urls", "http://*:5500"]