FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY FitnessApp/ FitnessApp/
COPY scripts/ scripts/
RUN dotnet publish FitnessApp/FitnessApp.csproj -c Release -p:BuildTailwind=false -o /app
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "FitnessApp.dll"]
