using Moq;
using Subscription_Service.Models;
using Subscription_Service.Services;
using Subscription_Service.Services.Interfaces;

namespace SubscriptionServiceTests
{
    /// <summary>
    /// Тести для SubscriptionService (RenewSubscription і DeactivateExpiredMembers).
    /// </summary>
    public class SubscriptionServiceTests
    {
        private const int MemberId = 1;
        private const int ExpiredMemberId = 2;
        private const int SecondExpiredMemberId = 3;
        private const int ActiveMemberId = 4;
        private const decimal PaymentAmount = 100m;
        private const int DefaultDays = 30;
        private const string RenewedMessage = "Subscription renewed!";
        private const string ExpiredMessage = "Membership expired";

        private readonly Mock<IMemberRepository> _repo = new();
        private readonly Mock<IPaymentService> _payment = new();
        private readonly Mock<INotificationService> _notify = new();

        /// <summary>
        /// Створює сервіс з mock-залежностями
        /// </summary>
        private SubscriptionService CreateService() =>
            new SubscriptionService(_repo.Object, _payment.Object, _notify.Object);

        /// <summary>
        /// Створює учасника для тесту
        /// </summary>
        private static Member CreateMember(int id, bool isActive, DateTime? subscriptionEnd) =>
            new Member
            {
                Id = id,
                Name = "Member " + id,
                IsActive = isActive,
                SubscriptionEnd = subscriptionEnd
            };

        /// <summary>
        /// Налаштовує репозиторій і оплату для RenewSubscription
        /// </summary>
        private void ArrangeRenewal(Member member, bool paymentAccepted)
        {
            _repo.Setup(r => r.GetById(MemberId)).Returns(member);
            _payment.Setup(p => p.VerifyPayment(MemberId, PaymentAmount)).Returns(paymentAccepted);
        }

        /// <summary>
        /// GetAll повертає переданих учасників
        /// </summary>
        private void ArrangeAllMembers(params Member[] members) =>
            _repo.Setup(r => r.GetAll()).Returns(members);

        /// <summary>
        /// Вибирає неактивних учасників
        /// </summary>
        private static List<Member> GetInactive(IEnumerable<Member> members) =>
            members.Where(m => !m.IsActive).ToList();

        /// <summary>
        /// Дата закінчення підписки = зараз + days
        /// </summary>
        [Theory]
        [InlineData(1)]
        [InlineData(30)]
        [InlineData(365)]
        public void RenewSubscription_ValidPayment_SetsSubscriptionEndAfterGivenDays(int days)
        {
            // Arrange
            var member = CreateMember(MemberId, false, null);
            ArrangeRenewal(member, true);
            var service = CreateService();

            // Act
            service.RenewSubscription(MemberId, PaymentAmount, days);

            // Assert
            Assert.Equal(DateTime.Now.AddDays(days), member.SubscriptionEnd!.Value, TimeSpan.FromSeconds(5));
        }

        /// <summary>
        /// Оплата пройшла - повертає true
        /// </summary>
        [Fact]
        public void RenewSubscription_ValidPayment_ReturnsTrue()
        {
            // Arrange
            var member = CreateMember(MemberId, true, DateTime.Now.AddDays(5));
            ArrangeRenewal(member, true);
            var service = CreateService();

            // Act
            var result = service.RenewSubscription(MemberId, PaymentAmount, DefaultDays);

            // Assert
            Assert.True(result);
        }

        /// <summary>
        /// Оплата не пройшла - повертає false
        /// </summary>
        [Fact]
        public void RenewSubscription_PaymentRejected_ReturnsFalse()
        {
            // Arrange
            var member = CreateMember(MemberId, true, DateTime.Now.AddDays(5));
            ArrangeRenewal(member, false);
            var service = CreateService();

            // Act
            var result = service.RenewSubscription(MemberId, PaymentAmount, DefaultDays);

            // Assert
            Assert.False(result);
        }

        /// <summary>
        /// Стара дата підписки змінюється на нову
        /// </summary>
        [Fact]
        public void RenewSubscription_ExpiredMember_ChangesSubscriptionEnd()
        {
            // Arrange
            var previousEnd = DateTime.Now.AddDays(-10);
            var member = CreateMember(MemberId, false, previousEnd);
            ArrangeRenewal(member, true);
            var service = CreateService();

            // Act
            service.RenewSubscription(MemberId, PaymentAmount, DefaultDays);

            // Assert
            Assert.NotEqual(previousEnd, member.SubscriptionEnd!.Value);
        }

        /// <summary>
        /// Якщо підписки не було, дата з'являється
        /// </summary>
        [Fact]
        public void RenewSubscription_MemberWithoutSubscription_SetsSubscriptionEnd()
        {
            // Arrange
            var member = CreateMember(MemberId, false, null);
            ArrangeRenewal(member, true);
            var service = CreateService();

            // Act
            service.RenewSubscription(MemberId, PaymentAmount, DefaultDays);

            // Assert
            Assert.NotNull(member.SubscriptionEnd);
        }

        /// <summary>
        /// Учасника немає - кидає ArgumentException
        /// </summary>
        [Fact]
        public void RenewSubscription_MemberNotFound_ThrowsArgumentException()
        {
            // Arrange
            _repo.Setup(r => r.GetById(MemberId)).Returns((Member?)null);
            var service = CreateService();

            // Act
            Action act = () => service.RenewSubscription(MemberId, PaymentAmount, DefaultDays);

            // Assert
            Assert.Throws<ArgumentException>(act);
        }

        /// <summary>
        /// Учасник зберігається в репозиторії
        /// </summary>
        [Fact]
        public void RenewSubscription_ValidPayment_UpdatesMemberInRepository()
        {
            // Arrange
            var member = CreateMember(MemberId, false, null);
            ArrangeRenewal(member, true);
            var service = CreateService();

            // Act
            service.RenewSubscription(MemberId, PaymentAmount, DefaultDays);

            // Assert
            _repo.Verify(r => r.Update(member));
        }

        /// <summary>
        /// У репозиторій йде активний учасник з датою в майбутньому
        /// </summary>
        [Fact]
        public void RenewSubscription_ValidPayment_UpdatesActiveMemberWithFutureEnd()
        {
            // Arrange
            var member = CreateMember(MemberId, false, null);
            ArrangeRenewal(member, true);
            var service = CreateService();

            // Act
            service.RenewSubscription(MemberId, PaymentAmount, DefaultDays);

            // Assert
            _repo.Verify(
                r => r.Update(It.Is<Member>(m => m.Id == MemberId && m.IsActive && m.SubscriptionEnd > DateTime.Now)),
                Times.Once());
        }

        /// <summary>
        /// Сповіщення надсилається один раз
        /// </summary>
        [Fact]
        public void RenewSubscription_ValidPayment_SendsNotificationOnce()
        {
            // Arrange
            var member = CreateMember(MemberId, false, null);
            ArrangeRenewal(member, true);
            var service = CreateService();

            // Act
            service.RenewSubscription(MemberId, PaymentAmount, DefaultDays);

            // Assert
            _notify.Verify(n => n.SendNotification(It.IsAny<string>(), MemberId), Times.Once());
        }

        /// <summary>
        /// Сповіщення з правильним текстом
        /// </summary>
        [Fact]
        public void RenewSubscription_ValidPayment_SendsRenewalMessageToMember()
        {
            // Arrange
            var member = CreateMember(MemberId, false, null);
            ArrangeRenewal(member, true);
            var service = CreateService();

            // Act
            service.RenewSubscription(MemberId, PaymentAmount, DefaultDays);

            // Assert
            _notify.Verify(n => n.SendNotification(RenewedMessage, MemberId));
        }

        /// <summary>
        /// Без оплати учасник не оновлюється
        /// </summary>
        [Fact]
        public void RenewSubscription_PaymentRejected_DoesNotUpdateMember()
        {
            // Arrange
            var member = CreateMember(MemberId, true, DateTime.Now.AddDays(5));
            ArrangeRenewal(member, false);
            var service = CreateService();

            // Act
            service.RenewSubscription(MemberId, PaymentAmount, DefaultDays);

            // Assert
            _repo.Verify(r => r.Update(It.IsAny<Member>()), Times.Never());
        }

        /// <summary>
        /// Двоє прострочених - Update викликається двічі
        /// </summary>
        [Fact]
        public void DeactivateExpiredMembers_TwoExpiredMembers_UpdatesRepositoryExactlyTwice()
        {
            // Arrange
            ArrangeAllMembers(
                CreateMember(ExpiredMemberId, true, DateTime.Now.AddDays(-1)),
                CreateMember(SecondExpiredMemberId, true, DateTime.Now.AddDays(-7)),
                CreateMember(ActiveMemberId, true, DateTime.Now.AddDays(10)));
            var service = CreateService();

            // Act
            service.DeactivateExpiredMembers();

            // Assert
            _repo.Verify(r => r.Update(It.IsAny<Member>()), Times.Exactly(2));
        }

        /// <summary>
        /// Прострочений учасник отримує сповіщення
        /// </summary>
        [Fact]
        public void DeactivateExpiredMembers_ExpiredMember_SendsExpiredNotificationAtLeastOnce()
        {
            // Arrange
            ArrangeAllMembers(CreateMember(ExpiredMemberId, true, DateTime.Now.AddDays(-1)));
            var service = CreateService();

            // Act
            service.DeactivateExpiredMembers();

            // Assert
            _notify.Verify(n => n.SendNotification(ExpiredMessage, ExpiredMemberId), Times.AtLeastOnce());
        }

        /// <summary>
        /// GetAll викликається не більше одного разу
        /// </summary>
        [Fact]
        public void DeactivateExpiredMembers_AnyMembers_ReadsRepositoryAtMostOnce()
        {
            // Arrange
            ArrangeAllMembers(
                CreateMember(ExpiredMemberId, true, DateTime.Now.AddDays(-1)),
                CreateMember(ActiveMemberId, true, DateTime.Now.AddDays(10)));
            var service = CreateService();

            // Act
            service.DeactivateExpiredMembers();

            // Assert
            _repo.Verify(r => r.GetAll(), Times.AtMost(1));
        }

        /// <summary>
        /// Прострочений учасник стає неактивним
        /// </summary>
        [Fact]
        public void DeactivateExpiredMembers_ExpiredMember_IsContainedInInactiveMembers()
        {
            // Arrange
            var expired = CreateMember(ExpiredMemberId, true, DateTime.Now.AddDays(-1));
            var active = CreateMember(ActiveMemberId, true, DateTime.Now.AddDays(10));
            ArrangeAllMembers(expired, active);
            var service = CreateService();

            // Act
            service.DeactivateExpiredMembers();

            // Assert
            Assert.Contains(expired, GetInactive(new[] { expired, active }));
        }

        /// <summary>
        /// Після деактивації є неактивні учасники
        /// </summary>
        [Fact]
        public void DeactivateExpiredMembers_ExpiredMember_InactiveMembersNotEmpty()
        {
            // Arrange
            var expired = CreateMember(ExpiredMemberId, true, DateTime.Now.AddDays(-1));
            ArrangeAllMembers(expired);
            var service = CreateService();

            // Act
            service.DeactivateExpiredMembers();

            // Assert
            Assert.NotEmpty(GetInactive(new[] { expired }));
        }

        /// <summary>
        /// Якщо підписки дійсні, нікого не деактивовано
        /// </summary>
        [Fact]
        public void DeactivateExpiredMembers_OnlyValidSubscriptions_InactiveMembersEmpty()
        {
            // Arrange
            var withFutureEnd = CreateMember(ActiveMemberId, true, DateTime.Now.AddDays(10));
            var withoutEnd = CreateMember(MemberId, true, null);
            ArrangeAllMembers(withFutureEnd, withoutEnd);
            var service = CreateService();

            // Act
            service.DeactivateExpiredMembers();

            // Assert
            Assert.Empty(GetInactive(new[] { withFutureEnd, withoutEnd }));
        }
    }
}
