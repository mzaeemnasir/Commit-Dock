using System.Collections.Concurrent;

namespace CommitDock.Models
{
    public class User
    {
        public static readonly User Invalid = new User(string.Empty);

        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;

        /// <summary>
        ///     The name to show for this user, falling back to the email address. Null when both are empty.
        /// </summary>
        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Name))
                    return Name;

                return string.IsNullOrWhiteSpace(Email) ? null : Email;
            }
        }

        public User(string data)
        {
            var parts = data.Split('±', 2);
            if (parts.Length < 2)
            {
                Email = data;
            }
            else
            {
                Name = parts[0];
                Email = parts[1];
            }
        }

        public static User FindOrAdd(string data)
        {
            return _caches.GetOrAdd(data, key => new User(key));
        }

        public override string ToString()
        {
            return $"{Name} <{Email}>";
        }

        private static ConcurrentDictionary<string, User> _caches = new ConcurrentDictionary<string, User>();
    }
}
