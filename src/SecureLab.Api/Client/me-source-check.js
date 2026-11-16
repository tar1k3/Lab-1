const ALICE_ID = "10000000-0000-0000-0000-000000000001";
const BOB_ID = "10000000-0000-0000-0000-000000000002";

const result = document.querySelector("#result");

document.querySelector("#login").addEventListener("submit", async (event) => {
  event.preventDefault();
  const form = event.currentTarget;
  const response = await fetch("/api/auth/login", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(Object.fromEntries(new FormData(form))),
  });
  form.elements.password.value = "";
  result.textContent = `Login: ${response.status}`;
});

document.querySelector("#check").addEventListener("click", async () => {
  const response = await fetch("/api/me", {
    headers: { "X-Demo-UserId": BOB_ID },
  });

  if (!response.ok) {
    result.textContent = JSON.stringify({ status: response.status });
    return;
  }

  const me = await response.json();
  result.textContent = JSON.stringify({
    status: response.status,
    isAlice: me.id === ALICE_ID,
    isBob: me.id === BOB_ID,
  });
});
