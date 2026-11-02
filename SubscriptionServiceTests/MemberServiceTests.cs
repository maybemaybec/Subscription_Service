using Moq;
using Subscription_Service.Models;
using Subscription_Service.Services;
using Subscription_Service.Services.Interfaces;

namespace SubscriptionServiceTests
{
    /// <summary>
    /// Тести для MemberService (GetMember і IsActive).
    /// </summary>
    public class MemberServiceTests
    {
        private const int MemberId = 1;
        private const int UnknownMemberId = 999;

        private readonly Mock<IMemberRepository> _repo = new();

        /// <summary>
        /// Створює сервіс з mock-залежностями
        /// </summary>
        private MemberService CreateService() => new MemberService(_repo.Object);

        /// <summary>
        /// Створює учасника для тесту
        /// </summary>
        private static Member CreateMember(int id, bool isActive) =>
            new Member { Id = id, Name = "Member " + id, IsActive = isActive };

        /// <summary>
        /// Повертає учасника з репозиторію
        /// </summary>
        [Fact]
        public void GetMember_ExistingId_ReturnsMemberFromRepository()
        {
            // Arrange
            var expected = CreateMember(MemberId, true);
            _repo.Setup(r => r.GetById(MemberId)).Returns(expected);
            var service = CreateService();

            // Act
            var actual = service.GetMember(MemberId);

            // Assert
            Assert.Equal(expected, actual);
        }

        /// <summary>
        /// Невідомий id - повертає null
        /// </summary>
        [Fact]
        public void GetMember_UnknownId_ReturnsNull()
        {
            // Arrange
            _repo.Setup(r => r.GetById(It.IsAny<int>())).Returns((Member?)null);
            var service = CreateService();

            // Act
            var result = service.GetMember(UnknownMemberId);

            // Assert
            Assert.Null(result);
        }

        /// <summary>
        /// Репозиторій викликається один раз
        /// </summary>
        [Fact]
        public void GetMember_AnyId_CallsRepositoryOnce()
        {
            // Arrange
            var service = CreateService();

            // Act
            service.GetMember(MemberId);

            // Assert
            _repo.Verify(r => r.GetById(It.IsAny<int>()), Times.Once());
        }

        /// <summary>
        /// Активний учасник - true
        /// </summary>
        [Fact]
        public void IsActive_ActiveMember_ReturnsTrue()
        {
            // Arrange
            _repo.Setup(r => r.GetById(MemberId)).Returns(CreateMember(MemberId, true));
            var service = CreateService();

            // Act
            var result = service.IsActive(MemberId);

            // Assert
            Assert.True(result);
        }

        /// <summary>
        /// Учасника немає - false
        /// </summary>
        [Fact]
        public void IsActive_UnknownMember_ReturnsFalse()
        {
            // Arrange
            _repo.Setup(r => r.GetById(UnknownMemberId)).Returns((Member?)null);
            var service = CreateService();

            // Act
            var result = service.IsActive(UnknownMemberId);

            // Assert
            Assert.False(result);
        }

        /// <summary>
        /// Повертає той самий статус, що й у учасника
        /// </summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void IsActive_MemberStatus_ReturnsSameStatus(bool isActive)
        {
            // Arrange
            _repo.Setup(r => r.GetById(MemberId)).Returns(CreateMember(MemberId, isActive));
            var service = CreateService();

            // Act
            var result = service.IsActive(MemberId);

            // Assert
            Assert.Equal(isActive, result);
        }

        /// <summary>
        /// Існуючий учасник - не null
        /// </summary>
        [Fact]
        public void GetMember_ExistingId_ReturnsNotNull()
        {
            // Arrange
            _repo.Setup(r => r.GetById(MemberId)).Returns(CreateMember(MemberId, true));
            var service = CreateService();

            // Act
            var result = service.GetMember(MemberId);

            // Assert
            Assert.NotNull(result);
        }
    }
}
