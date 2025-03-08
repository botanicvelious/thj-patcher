echo building....
set VERSION=0.0.0.1
set BUILD_CONFIGURATION=Release
set SERVER_NAME=The Heroes Journey
set FILE_NAME=heroesjourneyeq-test
set FILELIST_URL=https://github.com/The-Heroes-Journey-EQEMU/eqemupatcher/releases/latest/download
set PATCHER_URL=https://github.com/The-Heroes-Journey-EQEMU/eqemupatcher/releases/latest/download
"C:\Program Files\Microsoft Visual Studio\2022\Community\Msbuild\Current\Bin\msbuild.exe" /m /p:Configuration=%BUILD_CONFIGURATION% /p:VERSION=%VERSION% /p:SERVER_NAME="%SERVER_NAME%" /p:FILELIST_URL="%FILELIST_URL%" /p:PATCHER_URL="%PATCHER_URL%" /p:FILE_NAME="%FILE_NAME%"