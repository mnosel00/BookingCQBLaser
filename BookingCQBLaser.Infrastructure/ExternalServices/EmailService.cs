using BookingCQBLaser.Domain.Entities;
using BookingCQBLaser.Domain.Interfaces;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Text;

namespace BookingCQBLaser.Infrastructure.ExternalServices
{
    public class EmailService : IEmailService
    {
        private readonly SmtpOptions _smtpOptions;

        public EmailService(IOptions<SmtpOptions> options)
        {
            _smtpOptions = options.Value;
        }

        public async Task SendBookingConfirmationAsync(Booking booking, int totalCost, int depositAmount, int remainingBalance)
        {

            if (string.IsNullOrWhiteSpace(_smtpOptions.SenderEmail))
                throw new InvalidOperationException("SmtpOptions: SenderEmail is missing or null.");

            if (string.IsNullOrWhiteSpace(booking.Customer?.Email))
                throw new InvalidOperationException("Booking: Customer Email is missing or null.");
            totalCost+=4;
            var email = new MimeMessage();

            email.From.Add(new MailboxAddress(_smtpOptions.SenderName, _smtpOptions.SenderEmail));

            email.To.Add(new MailboxAddress(booking.Customer.FirstName ?? "Klient", booking.Customer.Email));

            email.Subject = "Potwierdzenie rezerwacji w Combo Arena";

            var paidAmount = depositAmount + 4;

            var htmlBody = $@"
                        <h2>Potwierdzenie rezerwacji w Combo Arena</h2>
                        <br/>
                        <h3>Szczegóły</h3>
                        <ul>
                            <li><strong>Data i godzina rezerwacji:</strong> {booking.StartTime:dd.MM.yyyy HH:mm}</li>
                            <li><strong>Cena:</strong> {totalCost} PLN</li>
                            <li><strong>Zapłacono:</strong> {paidAmount} PLN</li>
                            <li><strong>Do zapłaty na miejscu:</strong> {remainingBalance} PLN (TYLKO GOTÓWKĄ)</li>
                        </ul>

                        <h3>Lokalizacja i Kontakt</h3>
                        <p>
                            <strong>Adres:</strong> Prokocimska 8, wjazd od strony Dworca w Płaszowie.<br/>
                            <strong>Pinezka:</strong> <a href=""https://maps.app.goo.gl/W4GuAcdGxR21ez7s5"">Google Maps</a><br/>
                            <strong>Telefon:</strong> 509 595 199
                        </p>

                        <h3>Dokumenty do pobrania - należy wypełnić i przekazać intruktorowi w dniu imprezy</h3>
                        <p>W przypadku uczestnictwa osób niepełnoletnich wymagana jest zgoda rodzica/opiekuna oraz oświadczenie opiekuna grupy.</p>
                        <ul>
                            <li><a href=""https://comboarena.pl/wp-content/uploads/oswiadczenieopiekunagrupy.pdf"">Oświadczenie opiekuna grupy</a></li>
                            <li><a href=""https://comboarena.pl/wp-content/uploads/zgodarodzica.pdf"">Zgoda rodzica</a></li>
                        </ul>

                        <h3>Ogólne zasady uczestnictwa i rezerwacji</h3>
                        <ul>
                            <li>Wiek min. 10 lat</li>
                            <li>Min. liczba uczestników: 10 osób dla pakietów (S1/U1) lub 8 osób dla pozostałych</li>
                            <li>Zadatek 300 PLN</li>
                            <li>Płatność pozostałej kwoty na miejscu tylko gotówką</li>
                            <li>Punktualność wymagana</li>
                            <li>Osoby będące pod wpływem alkoholu nie będą wpuszczane na teren obiektu</li>
                        </ul>
                        <br/>
                        <p>Z pozdrowieniami,<br/>Zespół Combo Arena</p>";

            email.Body = new TextPart(TextFormat.Html) { Text = htmlBody };

            using var smtp = new SmtpClient();

            var secureSocketOptions = _smtpOptions.EnableSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;
            await smtp.ConnectAsync(_smtpOptions.Host, _smtpOptions.Port, secureSocketOptions);

            await smtp.AuthenticateAsync(_smtpOptions.Username, _smtpOptions.Password);
            await smtp.SendAsync(email);
            await smtp.DisconnectAsync(true);
        }
    }
}
