using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

[DisallowMultipleComponent]
public class SpatialWindowHandle : XRBaseInteractable
{
    public enum HandleKind
    {
        Drag,
        Resize
    }

    [SerializeField] private SpatialWindow owner;
    [SerializeField] private HandleKind handleKind = HandleKind.Drag;

    public HandleKind Kind => handleKind;

    public void Configure(SpatialWindow targetOwner, HandleKind kind)
    {
        owner = targetOwner;
        handleKind = kind;
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        selectEntered.AddListener(OnSelectEntered);
        selectExited.AddListener(OnSelectExited);
    }

    protected override void OnDisable()
    {
        selectEntered.RemoveListener(OnSelectEntered);
        selectExited.RemoveListener(OnSelectExited);
        base.OnDisable();
    }

    public override Transform GetAttachTransform(IXRInteractor interactor)
    {
        return transform;
    }

    public override void ProcessInteractable(XRInteractionUpdateOrder.UpdatePhase updatePhase)
    {
        base.ProcessInteractable(updatePhase);

        if (updatePhase != XRInteractionUpdateOrder.UpdatePhase.Dynamic)
        {
            return;
        }

        if (!isSelected || owner == null || interactorsSelecting.Count == 0)
        {
            return;
        }

        owner.ProcessHandleSelection(this, interactorsSelecting[0]);
    }

    private void OnSelectEntered(SelectEnterEventArgs args)
    {
        owner?.OnHandleSelectEntered(this, args.interactorObject);
    }

    private void OnSelectExited(SelectExitEventArgs args)
    {
        owner?.OnHandleSelectExited(this, args.interactorObject);
    }
}
