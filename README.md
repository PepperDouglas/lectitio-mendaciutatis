# Lectitio Mendaciutatis

A secure, real-time chat application built with Blazor server, SignalR and EF Core. Featuring Auth, encryption, private rooms and more.

## Local Development:

For local development, we use a self-signed SSL certificate to secure communication over HTTPS. Each developer should generate their own certificate for their machine. Follow the steps below to create and install the certificate.

### Generate a Self-Signed Certificate

(Add a certs/ folder to root)

Open **PowerShell** as Administrator and run the following command to generate a self-signed certificate:

$cert = New-SelfSignedCertificate -DnsName "localhost" -CertStoreLocation "cert:\LocalMachine\My"

Get-ChildItem -Path cert:\LocalMachine\My

**Replace [your-pfx-password] with your own password**

$pwd = ConvertTo-SecureString -String "your-pfx-password" -Force -AsPlainText

Export-PfxCertificate -Cert cert:\LocalMachine\My\$($cert.Thumbprint) -FilePath "C:\path\to\your\project\certs\selfsigned.pfx" -Password $pwd

Navigate to Trusted Root Certification Authorities on your machine and add the exported cert to trusted root store with **certlm.msc**

### In Program.cs replace [your-pfx-password] with the one you chose

options.ListenAnyIP(5001, listenOptions =>
    {
        listenOptions.UseHttps("certs/selfsigned.pfx", "your-pfx-password");
    });

### Create the database if it's not already set up

dotnet ef database update

### Start application

dotnet restore

dotnet run

### If you get CryptographicException =>

In Admin PowerShell write: icacls C:\ProgramData\Microsoft\Crypto\RSA\MachineKeys /inheritance:r /grant Administrators:F /grant:r Everyone:RW
