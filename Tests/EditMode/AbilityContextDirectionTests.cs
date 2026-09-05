using NUnit.Framework;
using UnityEngine;
using Fofuxo.GameplayAbilitySystem;

namespace Fofuxo.GameplayAbilitySystem.Tests
{
    public sealed class AbilityContextDirectionTests
    {
        [Test]
        public void FromDirection_ProjectsSuppliedDirectionOntoGroundPlane()
        {
            GameObject owner = new("AbilityContextOwner");
            try
            {
                owner.transform.position = Vector3.zero;
                AbilityContext context = AbilityContext.FromDirection(
                    owner,
                    null,
                    new Vector3(3f, 5f, 4f));

                Assert.AreEqual(0f, context.Direction.y, 0.0001f);
                Vector3 expectedDirection = new Vector3(3f, 0f, 4f).normalized;
                Assert.AreEqual(expectedDirection.x, context.Direction.x, 0.0001f);
                Assert.AreEqual(expectedDirection.z, context.Direction.z, 0.0001f);
                Assert.IsNull(context.Target);
                Assert.AreSame(owner, context.Owner);
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void FromDirection_FallsBackToOwnerForwardWhenDirectionIsEmpty()
        {
            GameObject owner = new("AbilityContextOwner");
            try
            {
                owner.transform.forward = Vector3.right;
                AbilityContext context = AbilityContext.FromDirection(
                    owner,
                    null,
                    Vector3.zero);

                Assert.AreEqual(Vector3.right.x, context.Direction.x, 0.0001f);
                Assert.AreEqual(Vector3.right.y, context.Direction.y, 0.0001f);
                Assert.AreEqual(Vector3.right.z, context.Direction.z, 0.0001f);
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }
    }
}
