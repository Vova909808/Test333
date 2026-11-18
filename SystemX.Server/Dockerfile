
# Этап 1. Сборка приложения
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# Копирование проекта
COPY SystemX.Server.csproj ./

# Восстановление зависимостей
RUN dotnet restore

# Копирование остальных файлов
COPY . .

# Публикация приложения
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false


# Этап 2. Запуск приложения
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

WORKDIR /app

COPY --from=build /app/publish .

# Порт Render
ENV ASPNETCORE_URLS=http://+:10000

EXPOSE 10000

# Запуск сервера
ENTRYPOINT ["dotnet", "SystemX.Server.dll"]
