@echo off
chcp 65001 >nul
title Compilando rrv.o.s
echo ==================================================
echo   rrv.o.s - gerando o executavel (.exe)
echo ==================================================
echo.

where dotnet >nul 2>nul
if errorlevel 1 (
  echo [ERRO] O SDK do .NET 8 nao foi encontrado.
  echo Baixe e instale em: https://dotnet.microsoft.com/download/dotnet/8.0
  echo Escolha ".NET SDK 8.x" para Windows x64, instale e rode este arquivo de novo.
  echo.
  pause
  exit /b 1
)

cd /d "%~dp0"
if exist publicar rmdir /s /q publicar

dotnet publish rrv.o.s.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtraction=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:DebugType=none ^
  -o publicar

if errorlevel 1 (
  echo.
  echo [ERRO] A compilacao falhou. Copie as mensagens acima e me envie para eu corrigir.
  pause
  exit /b 1
)

echo.
echo ==================================================
echo   PRONTO!  O executavel esta em:
echo   %~dp0publicar\rrv.o.s.exe
echo ==================================================
echo Copie o rrv.o.s.exe para qualquer pasta e de dois cliques para abrir.
echo.
pause
