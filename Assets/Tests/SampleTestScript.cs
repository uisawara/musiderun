using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

public class SampleTestScript
{
    // A Test behaves as an ordinary method
    [Test]
    public void NewTestScriptSimplePasses()
    {
        // Use the Assert class to test conditions
        throw new Exception();
    }

    // A UnityTest behaves like a coroutine in Play Mode. In Edit Mode you can use
    // `yield return null;` to skip a frame.
    [UnityTest]
    public IEnumerator NewTestScriptWithEnumeratorPasses()
    {
        throw new Exception();

        // Use the Assert class to test conditions.
        // Use yield to skip a frame.
        yield return null;
    }
}
