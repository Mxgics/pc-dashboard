window.pcDashboard = {
  showModal(dialog, initialFocusId) {
    if (!dialog.open) dialog.showModal();
    requestAnimationFrame(() => document.getElementById(initialFocusId)?.focus());
  },
  closeModal(dialog) {
    if (dialog?.open) dialog.close();
  },
  focusById(id) {
    requestAnimationFrame(() => document.getElementById(id)?.focus());
  }
};
