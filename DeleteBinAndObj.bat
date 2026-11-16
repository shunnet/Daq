@echo off

set "targetFolders=bin obj"  REM 要删除的目标文件夹列表，用空格分隔

for %%F in (%targetFolders%) do (
    call :DeleteFolder "%%~F"
)

exit /b

:DeleteFolder
for /d /r %%D in (*) do (
    if "%%~nxD"=="%~1" (
        echo Deleting folder: "%%~fD"
        rmdir /s /q "%%~fD"
    )
)
exit /b
