const express = require("express");
const path = require("path");

const app = express();
const PORT = process.env.PORT || 3000;

// Serve static files (React build) from the "dist" folder
app.use(express.static(path.join(__dirname, "dist")));

// API route example
app.get("/api/message", (req, res) => {
  res.json({ message: "Hello from Express!" });
});

// Fallback route to serve index.html for all other routes
// This lets React Router handle frontend routing
app.get("*", (req, res) => {
  res.sendFile(path.join(__dirname, "dist", "index.html"));
});

app.listen(PORT, () => {
  console.log(`Server is running on http://localhost:${PORT}`);
});
