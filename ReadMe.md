

## Deployed ScannerInterface3.dll, NTwain.dll, and PDFSharp.pdf may be blocked by Windows.
Solution is to check the copied destination, do File Properties -check Unblock checkox. Dll is unblock if Unblock checkbox does not appear. But beware it may still be blocked when copied from shared network or downloaded from internet.

Or, on client PC, set \\appinit.rejis.org as trused. Internet options -> Intranet -> Security -> add \\appinit.rejis.org

Debugging PowerBuilder proxy class nuo_twainscanner showed this error:
Error Number: -3. Failed to load scannerinterface3.dll

Error Text: An attempt was made to load an assembly fron a network location which would have caused the assembly to be sandboxed in previous versions of the .NET Framework. This release of the .NET Framework does not enable CAS policy by default, so this load may be dangerous. If this load is not intended to sandbox the assembly, please enable the loadFromRemoteSources switch. See http://go.microsoft.com/fwlink/?LinkId=155569 for more information._



## 🛞 Ricoh fi-8170 driver download for 32-bit and 64-bit
Get 32 and 64 bit PaperStream IP (TWAIN).

### 64-bit Driver Download
https://www.pfu.ricoh.com/global/scanners/fi/dl/setup/psip-twain64-3401.html

### 32-bit Driver Download
https://www.pfu.ricoh.com/global/scanners/fi/dl/win-11-fi-8x70.html

Troubleshooting:   
https://fi-faq.pfu.ricoh.com/hc/en-us/articles/13322763972121-Is-there-the-PaperStream-IP-for-64-bit-Windows

---

## 💪 Strong‑Name the Assembly
PowerBuilder requires **signed DLLs** for reliable loading.

1. Generate a key pair, then add the new file ScannerInteropLib.snk to this project:   
Open command developer prompt, go to the project folder.
   ```
   sn -k ScannerInterface3.snk
   ```
2. Go to Project properties → **Signing** tab:   
Check Sign the assembly and → Browse → Select `ScannerInterface3.snk`.

   
3. Rebuild → DLL is now strong‑named.

4. For deployment, Rebuild using Release -> ***Any CPU*** configuration.   
NTwain does not require separate x86/x64 builds unless native dependencies exist.


```Get the DLLs here:   
bin\Release\ScannerInterface3.dll
bin\Release\NTwain.dll
```

---

### PowerBuilder IDE 2025 Integration and Deployment
- Copy ```bin\Release\NTwain.dll```
- Copy ```bin\Release\PdfSharp.dll```
- Copy ```bin\Release\ScannerInterface3.dll``` into PowerBuilder 2025 workspace folder.   
- Use .NET DLL Importer to create proxy class.
- On created proxy class, update of_createondemand() replace "LoadWithDotNet" with "LoadWithDotNetFramework"
- Include all DLLs in your PowerBuilder application deployment.

--


