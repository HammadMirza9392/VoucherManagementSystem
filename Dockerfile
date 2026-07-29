FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["VoucherManagementSystem/VoucherManagementSystem.csproj", "VoucherManagementSystem/"]
RUN dotnet restore "VoucherManagementSystem/VoucherManagementSystem.csproj"
COPY . .
WORKDIR "/src/VoucherManagementSystem"
RUN dotnet publish "VoucherManagementSystem.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
# Linux hosts cap inotify instances (128 on Render); polling avoids exhausting them.
ENV DOTNET_USE_POLLING_FILE_WATCHER=true
EXPOSE 8080
ENTRYPOINT ["dotnet", "VoucherManagementSystem.dll"]
