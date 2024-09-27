# Lectitio Mendaciutatis

## Local Development: Self-Signed SSL Certificate Setup

For local development, we use a self-signed SSL certificate to secure communication over HTTPS. Each developer should generate their own certificate for their machine. Follow the steps below to create and install the certificate.

### Generate a Self-Signed Certificate

(Add a certs/ folder to root)

Open **PowerShell** as Administrator and run the following command to generate a self-signed certificate:

New-SelfSignedCertificate -DnsName "localhost" -CertStoreLocation "cert:\LocalMachine\My"

Get-ChildItem -Path cert:\LocalMachine\My

$pwd = ConvertTo-SecureString -String "your-pfx-password" -Force -AsPlainText

Export-PfxCertificate -Cert cert:\LocalMachine\My\THUMBPRINT -FilePath"C:\path\to\your\project\certs\selfsigned.pfx" -Password $pwd

### In Program.cs

options.ListenAnyIP(5001, listenOptions =>
    {
        listenOptions.UseHttps("certs/selfsigned.pfx", "your-pfx-password");
    });