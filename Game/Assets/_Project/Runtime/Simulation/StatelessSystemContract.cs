using System;
using System.Collections.Generic;
using System.Reflection;

namespace CML.Simulation
{
    /// <summary>
    /// Enforces the rollback contract at registration: systems can contain only
    /// deeply immutable value/string configuration. Mutable fields, collections,
    /// delegates and other reference graphs are rejected before the first tick.
    /// </summary>
    internal static class StatelessSystemContract
    {
        public static void Validate(ISimulationPhaseSystem system)
        {
            var type = system.GetType();
            for (var current = type; current != null && current != typeof(object); current = current.BaseType)
            {
                var fields = current.GetFields(
                    BindingFlags.Instance
                    | BindingFlags.Static
                    | BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly);
                for (var index = 0; index < fields.Length; index++)
                {
                    var field = fields[index];
                    if (field.IsStatic && field.IsLiteral)
                    {
                        continue;
                    }

                    if (!field.IsInitOnly || !IsDeeplyImmutable(field.FieldType, new HashSet<Type>()))
                    {
                        throw new ArgumentException(
                            $"Simulation system '{type.FullName}' violates the stateless contract at "
                            + $"field '{field.Name}' ({field.FieldType.FullName}). "
                            + "Future-affecting state belongs in SimulationState.");
                    }
                }
            }
        }

        private static bool IsDeeplyImmutable(Type type, ISet<Type> visiting)
        {
            if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal))
            {
                return true;
            }

            if (!type.IsValueType)
            {
                return false;
            }

            if (!visiting.Add(type))
            {
                return true;
            }

            var fields = type.GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (var index = 0; index < fields.Length; index++)
            {
                if (!fields[index].IsInitOnly
                    || !IsDeeplyImmutable(fields[index].FieldType, visiting))
                {
                    return false;
                }
            }

            visiting.Remove(type);
            return true;
        }
    }
}
