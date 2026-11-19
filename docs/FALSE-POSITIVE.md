# Reporting a false positive

If your antivirus flags Hide Any Apps, the fastest fix is a report to the
vendor. It costs nothing, requires no special access, and typically clears
within days. Include the SHA-256 from `SHA256SUMS.txt` on the
[latest release](https://github.com/ziyyhhk/Hide-Any-Apps/releases/latest).

## Microsoft Defender (includes SmartScreen)

1. Go to <https://www.microsoft.com/en-us/wdsi/filesubmission> and sign in
   with any Microsoft account.
2. Choose **I am a software developer** → *Incorrectly detected as
   malware/unwanted*.
3. Upload `HideAnyApps-Setup.exe` (or the flagged `HideAnyWindowService.exe`
   / `HideAnyWindowManager.exe`).
4. Use this text (adapt as needed):

   > Hide Any Apps (https://github.com/ziyyhhk/Hide-Any-Apps) is an MIT-licensed,
   > open-source Windows utility that hides selected applications' windows from
   > the screen, taskbar and Alt-Tab. It is not malware. The service binary is
   > built from the AutoHotkey v2 source in `service/` in the repository. The
   > installer is a per-user Inno Setup package and does not modify any
   > security store, proxy, hosts file or browser settings. Please re-evaluate
   > the detection. SHA-256 of the flagged file: &lt;paste from SHA256SUMS.txt&gt;.

5. Microsoft publishes updated signatures; check the result email and rescan
   after a few days.

## Malwarebytes

1. Go to <https://forums.malwarebytes.com/forum/61-false-positives/64-report-a-false-positive/>
   (or the forum at forums.malwarebytes.com → *False Positives →
   Report a False Positive*).
2. Create a thread with the same description as above plus the file's SHA-256
   and a VirusTotal link if you have one.
3. Staff will confirm and push a database update.

## Everything else

- **Bitdefender**: <https://www.bitdefender.com/consumer/support/answer/29358/>
- **Kaspersky**: <https://support.kaspersky.com/general/contacts> → *False
  positive report* (select *I have a file that is detected incorrectly*).
- **Avast/AVG**: <https://www.avast.com/contact-form.php> → *Report false positive*.
- **ESET**: <https://www.eset.com/int/support/detection_report/>

If you are comfortable, also upload the file to
<https://www.virustotal.com/gui/home/upload> and comment on the analysis with
a link to this repository — analyst teams at several vendors review VT
comments.
